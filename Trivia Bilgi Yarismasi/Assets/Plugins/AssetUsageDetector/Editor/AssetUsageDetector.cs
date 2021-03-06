// Asset Usage Detector - by Suleyman Yasir KULA (yasirkula@gmail.com)

using AssetUsageDetectorNamespace.Extras;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;
using System.IO;
using UnityEngine.UI;
#if UNITY_2017_1_OR_NEWER
using UnityEngine.U2D;
#if UNITY_2018_2_OR_NEWER
using UnityEditor.U2D;
#endif
using UnityEngine.Playables;
#endif
#if UNITY_2017_2_OR_NEWER
using UnityEngine.Tilemaps;
#endif
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	[Flags]
	public enum SceneSearchMode { None = 0, OpenScenes = 1, ScenesInBuildSettingsAll = 2, ScenesInBuildSettingsTickedOnly = 4, AllScenes = 8 };
	public enum PathDrawingMode { Full = 0, ShortRelevantParts = 1, Shortest = 2 };

	public class AssetUsageDetector
	{
		#region Helper Classes
		public class Parameters
		{
			public IEnumerable<Object> objectsToSearch;

			public SceneSearchMode searchInScenes;
			public bool searchInAssetsFolder;
			public IEnumerable<Object> searchInAssetsSubset;
			public IEnumerable<Object> excludedAssetsFromSearch;
			public bool dontSearchInSourceAssets;
			public IEnumerable<Object> excludedScenesFromSearch;

			public int searchDepthLimit;
			public BindingFlags fieldModifiers, propertyModifiers;

			public bool searchNonSerializableVariables;
			public bool noAssetDatabaseChanges;
			public bool showProgressBar;

			public Parameters()
			{
				objectsToSearch = null;
				searchInScenes = SceneSearchMode.AllScenes;
				searchInAssetsFolder = true;
				searchInAssetsSubset = null;
				excludedAssetsFromSearch = null;
				dontSearchInSourceAssets = false;
				excludedScenesFromSearch = null;
				searchDepthLimit = 4;
				fieldModifiers = BindingFlags.Public | BindingFlags.NonPublic;
				propertyModifiers = BindingFlags.Public | BindingFlags.NonPublic;
				searchNonSerializableVariables = true;
				noAssetDatabaseChanges = false;
				showProgressBar = false;
			}
		}

		private class CacheEntry
		{
			public enum Result { Unknown = 0, No = 1, Yes = 2 };

			public string hash;
			public string[] dependencies;
			public long[] fileSizes;

			public bool verified;
			public Result searchResult;

			public CacheEntry( string path )
			{
				Verify( path );
			}

			public CacheEntry( string hash, string[] dependencies, long[] fileSizes )
			{
				this.hash = hash;
				this.dependencies = dependencies;
				this.fileSizes = fileSizes;
			}

			public void Verify( string path )
			{
				string hash = AssetDatabase.GetAssetDependencyHash( path ).ToString();
				if( this.hash != hash )
				{
					this.hash = hash;
					Refresh( path );
				}

				verified = true;
			}

			public void Refresh( string path )
			{
				dependencies = AssetDatabase.GetDependencies( path, false );
				if( fileSizes == null || fileSizes.Length != dependencies.Length )
					fileSizes = new long[dependencies.Length];

				for( int i = 0; i < dependencies.Length; i++ )
					fileSizes[i] = new FileInfo( dependencies[i] ).Length;
			}
		}
		#endregion

		private HashSet<Object> objectsToSearchSet; // A set that contains the searched asset(s) and their sub-assets (if any)

		private HashSet<Object> sceneObjectsToSearchSet; // Scene object(s) in objectsToSearchSet
		private HashSet<string> sceneObjectsToSearchScenesSet; // sceneObjectsToSearchSet's scene(s)

		private HashSet<Object> assetsToSearchSet; // Project asset(s) in objectsToSearchSet
		private HashSet<string> assetsToSearchPathsSet; // assetsToSearchSet's path(s)

		private SearchResultGroup currentSearchResultGroup; // Results for the currently searched scene

		private Dictionary<Type, VariableGetterHolder[]> typeToVariables; // An optimization to fetch & filter fields and properties of a class only once
		private Dictionary<string, ReferenceNode> searchedObjects; // An optimization to search an object only once (key is a hash of the searched object)
		private Dictionary<string, CacheEntry> assetDependencyCache; // An optimization to fetch the dependencies of an asset only once (key is the path of the asset)

		private Dictionary<Type, Func<Object, ReferenceNode>> typeToSearchFunction; // Dictionary to quickly find the function to search a specific type with

		private List<object> callStack; // Stack of SearchObject function parameters to avoid infinite loops (which happens when same object is passed as parameter to function)

		private bool searchPrefabConnections;
		private bool searchMonoBehavioursForScript;
		private bool searchRenderers;
		private bool searchMaterialsForShader;
		private bool searchMaterialsForTexture;

		private bool searchSerializableVariablesOnly;

		private int searchDepthLimit; // Depth limit for recursively searching variables of objects

		private Object currentObject;
		private int currentDepth;

		private BindingFlags fieldModifiers, propertyModifiers;
		private BindingFlags prevFieldModifiers, prevPropertyModifiers;

		private int searchedObjectsCount; // Number of searched objects
		private double searchStartTime;

		private List<ReferenceNode> nodesPool = new List<ReferenceNode>( 32 );
		private List<VariableGetterHolder> validVariables = new List<VariableGetterHolder>( 32 );

		private string CachePath { get { return Application.dataPath + "/../Library/AssetUsageDetector.cache"; } } // Path of the cache file

		// Search for references!
		public SearchResult Run( Parameters searchParameters )
		{
			if( searchParameters.objectsToSearch == null )
			{
				Debug.LogError( "objectsToSearch list is empty" );
				return new SearchResult( false, null, null );
			}

			List<SearchResultGroup> searchResult = null;

			// Get the scenes that are open right now
			SceneSetup[] initialSceneSetup = !EditorApplication.isPlaying ? EditorSceneManager.GetSceneManagerSetup() : null;

			// Make sure the AssetDatabase is up-to-date
			AssetDatabase.SaveAssets();

			try
			{
				currentDepth = 0;
				searchedObjectsCount = 0;
				searchStartTime = EditorApplication.timeSinceStartup;

				this.fieldModifiers = searchParameters.fieldModifiers | BindingFlags.Instance | BindingFlags.DeclaredOnly;
				this.propertyModifiers = searchParameters.propertyModifiers | BindingFlags.Instance | BindingFlags.DeclaredOnly;
				this.searchDepthLimit = searchParameters.searchDepthLimit;

				// Initialize commonly used variables
				searchResult = new List<SearchResultGroup>(); // Overall search results

				if( typeToVariables == null )
					typeToVariables = new Dictionary<Type, VariableGetterHolder[]>( 4096 );
				else if( prevFieldModifiers != fieldModifiers || prevPropertyModifiers != propertyModifiers )
					typeToVariables.Clear();

				if( searchedObjects == null )
					searchedObjects = new Dictionary<string, ReferenceNode>( 32768 );
				else
					searchedObjects.Clear();

				if( callStack == null )
					callStack = new List<object>( 64 );
				else
					callStack.Clear();

				if( objectsToSearchSet == null )
					objectsToSearchSet = new HashSet<Object>();
				else
					objectsToSearchSet.Clear();

				if( sceneObjectsToSearchSet == null )
					sceneObjectsToSearchSet = new HashSet<Object>();
				else
					sceneObjectsToSearchSet.Clear();

				if( sceneObjectsToSearchScenesSet == null )
					sceneObjectsToSearchScenesSet = new HashSet<string>();
				else
					sceneObjectsToSearchScenesSet.Clear();

				if( assetsToSearchSet == null )
					assetsToSearchSet = new HashSet<Object>();
				else
					assetsToSearchSet.Clear();

				if( assetsToSearchPathsSet == null )
					assetsToSearchPathsSet = new HashSet<string>();
				else
					assetsToSearchPathsSet.Clear();

				if( assetDependencyCache == null )
				{
					LoadCache();
					searchStartTime = EditorApplication.timeSinceStartup;
				}
				else if( !searchParameters.noAssetDatabaseChanges )
				{
					foreach( var cacheEntry in assetDependencyCache.Values )
						cacheEntry.verified = false;
				}

				foreach( var cacheEntry in assetDependencyCache.Values )
					cacheEntry.searchResult = CacheEntry.Result.Unknown;

				if( typeToSearchFunction == null )
				{
					typeToSearchFunction = new Dictionary<Type, Func<Object, ReferenceNode>>()
					{
						{ typeof( GameObject ), SearchGameObject },
						{ typeof( Material ), SearchMaterial },
						{ typeof( RuntimeAnimatorController ), SearchAnimatorController },
						{ typeof( AnimatorOverrideController ), SearchAnimatorController },
						{ typeof( AnimatorController ), SearchAnimatorController },
						{ typeof( AnimatorStateMachine ), SearchAnimatorStateMachine },
						{ typeof( AnimatorState ), SearchAnimatorState },
						{ typeof( AnimatorStateTransition ), SearchAnimatorStateTransition },
						{ typeof( BlendTree ), SearchBlendTree },
						{ typeof( AnimationClip ), SearchAnimationClip },
#if UNITY_2017_1_OR_NEWER
						{ typeof( SpriteAtlas ), SearchSpriteAtlas },
#endif
					};
				}

				prevFieldModifiers = fieldModifiers;
				prevPropertyModifiers = propertyModifiers;

				searchPrefabConnections = false;
				searchMonoBehavioursForScript = false;
				searchRenderers = false;
				searchMaterialsForShader = false;
				searchMaterialsForTexture = false;

				// Store the searched objects(s) in HashSets
				HashSet<string> folderContentsSet = new HashSet<string>();
				foreach( Object obj in searchParameters.objectsToSearch )
				{
					if( obj == null || obj.Equals( null ) )
						continue;

					if( obj.IsFolder() )
						folderContentsSet.UnionWith( Utilities.EnumerateFolderContents( obj ) );
					else
						AddSearchedObjectToFilteredSets( obj );
				}

				foreach( string filePath in folderContentsSet )
				{
					// Skip scene assets
					if( filePath.EndsWith( ".unity" ) )
						continue;

					Object[] assets = AssetDatabase.LoadAllAssetsAtPath( filePath );
					if( assets == null || assets.Length == 0 )
						continue;

					for( int i = 0; i < assets.Length; i++ )
						AddSearchedObjectToFilteredSets( assets[i] );
				}

				foreach( Object obj in objectsToSearchSet )
				{
					if( obj is Texture )
					{
						searchRenderers = true;
						searchMaterialsForTexture = true;
					}
					else if( obj is Material )
					{
						searchRenderers = true;
					}
					else if( obj is MonoScript )
					{
						searchMonoBehavioursForScript = true;
					}
					else if( obj is Shader )
					{
						searchRenderers = true;
						searchMaterialsForShader = true;
					}
					else if( obj is GameObject )
					{
						searchPrefabConnections = true;
					}
				}

				// Find the scenes to search for references
				HashSet<string> openScenes = null;
				if( EditorApplication.isPlaying )
				{
					// In Play mode, only open scenes can be searched
					openScenes = new HashSet<string>();
					for( int i = 0; i < EditorSceneManager.loadedSceneCount; i++ )
					{
						Scene scene = EditorSceneManager.GetSceneAt( i );
						if( scene.IsValid() )
							openScenes.Add( scene.path );
					}
				}

				HashSet<string> scenesToSearch = new HashSet<string>();
				if( ( searchParameters.searchInScenes & SceneSearchMode.AllScenes ) == SceneSearchMode.AllScenes )
				{
					// Get all scenes from the Assets folder
					string[] sceneGuids = AssetDatabase.FindAssets( "t:SceneAsset" );
					for( int i = 0; i < sceneGuids.Length; i++ )
					{
						string scenePath = AssetDatabase.GUIDToAssetPath( sceneGuids[i] );
						if( !EditorApplication.isPlaying || openScenes.Contains( scenePath ) )
							scenesToSearch.Add( scenePath );
					}
				}
				else
				{
					if( ( searchParameters.searchInScenes & SceneSearchMode.OpenScenes ) == SceneSearchMode.OpenScenes )
					{
						// Get all open (and loaded) scenes
						for( int i = 0; i < EditorSceneManager.loadedSceneCount; i++ )
						{
							Scene scene = EditorSceneManager.GetSceneAt( i );
							if( scene.IsValid() )
								scenesToSearch.Add( scene.path );
						}
					}

					bool searchInScenesInBuildTickedAll = ( searchParameters.searchInScenes & SceneSearchMode.ScenesInBuildSettingsAll ) == SceneSearchMode.ScenesInBuildSettingsAll;
					if( searchInScenesInBuildTickedAll || ( searchParameters.searchInScenes & SceneSearchMode.ScenesInBuildSettingsTickedOnly ) == SceneSearchMode.ScenesInBuildSettingsTickedOnly )
					{
						// Get all scenes in build settings
						EditorBuildSettingsScene[] scenesTemp = EditorBuildSettings.scenes;
						for( int i = 0; i < scenesTemp.Length; i++ )
						{
							if( ( searchInScenesInBuildTickedAll || scenesTemp[i].enabled ) && ( !EditorApplication.isPlaying || openScenes.Contains( scenesTemp[i].path ) ) )
								scenesToSearch.Add( scenesTemp[i].path );
						}
					}
				}

				// By default, search only serializable variables for references
				searchSerializableVariablesOnly = !searchParameters.searchNonSerializableVariables;

				// Initialize the nodes of searched asset(s)
				foreach( Object obj in objectsToSearchSet )
				{
					BeginSearchObject( obj );

					string objHash = obj.Hash();
					ReferenceNode referenceNode;
					if( !searchedObjects.TryGetValue( objHash, out referenceNode ) || referenceNode == null )
						searchedObjects[objHash] = PopReferenceNode( obj );
				}

				// Progressbar values
				int searchProgress = 0;
				int searchTotalProgress = scenesToSearch.Count;
				if( EditorApplication.isPlaying )
					searchTotalProgress++; // DontDestroyOnLoad scene

				// Don't search assets if searched object(s) are all scene objects as assets can't hold references to scene objects
				if( searchParameters.searchInAssetsFolder && assetsToSearchSet.Count > 0 )
				{
					currentSearchResultGroup = new SearchResultGroup( "Project View (Assets)", false );

					// Get the paths of all assets that are to be searched
					IEnumerable<string> assetPaths;
					if( searchParameters.searchInAssetsSubset == null )
					{
						string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
						searchTotalProgress += allAssetPaths.Length;
						assetPaths = allAssetPaths;
					}
					else
					{
						folderContentsSet.Clear();

						foreach( Object obj in searchParameters.searchInAssetsSubset )
						{
							if( obj == null || obj.Equals( null ) )
								continue;

							if( !obj.IsAsset() )
								continue;

							if( obj.IsFolder() )
								folderContentsSet.UnionWith( Utilities.EnumerateFolderContents( obj ) );
							else
								folderContentsSet.Add( AssetDatabase.GetAssetPath( obj ) );
						}

						searchTotalProgress += folderContentsSet.Count;
						assetPaths = folderContentsSet;
					}

					// Calculate the path(s) of the assets that won't be searched for references
					HashSet<string> excludedAssetsPathsSet = new HashSet<string>();
					if( searchParameters.excludedAssetsFromSearch != null )
					{
						foreach( Object obj in searchParameters.excludedAssetsFromSearch )
						{
							if( obj == null || obj.Equals( null ) )
								continue;

							if( !obj.IsAsset() )
								continue;

							if( obj.IsFolder() )
								excludedAssetsPathsSet.UnionWith( Utilities.EnumerateFolderContents( obj ) );
							else
								excludedAssetsPathsSet.Add( AssetDatabase.GetAssetPath( obj ) );
						}
					}

					if( searchParameters.dontSearchInSourceAssets )
						excludedAssetsPathsSet.UnionWith( assetsToSearchPathsSet );

					foreach( string path in assetPaths )
					{
						if( searchParameters.showProgressBar && ++searchProgress % 30 == 1 && EditorUtility.DisplayCancelableProgressBar( "Please wait...", "Searching assets", (float) searchProgress / searchTotalProgress ) )
							throw new Exception( "Search aborted" );

						if( excludedAssetsPathsSet.Contains( path ) )
							continue;

						// If asset resides inside the Assets directory and is not a scene asset
						if( path.StartsWith( "Assets/" ) && !path.EndsWith( ".unity" ) )
						{
							if( !AssetHasAnyReference( path ) )
								continue;

							Object[] assets = AssetDatabase.LoadAllAssetsAtPath( path );
							if( assets == null || assets.Length == 0 )
								continue;

							for( int i = 0; i < assets.Length; i++ )
							{
								// Components are already searched while searching the GameObject
								if( assets[i] is Component )
									continue;

								BeginSearchObject( assets[i] );
							}
						}
					}

					// If a reference is found in the Project view, save the results
					if( currentSearchResultGroup.NumberOfReferences > 0 )
						searchResult.Add( currentSearchResultGroup );
				}

				// Search non-serializable variables for references only if we are currently searching a scene and the editor is in play mode
				if( EditorApplication.isPlaying )
					searchSerializableVariablesOnly = false;

				if( scenesToSearch.Count > 0 )
				{
					// Calculate the path(s) of the scenes that won't be searched for references
					HashSet<string> excludedScenesPathsSet = new HashSet<string>();
					if( searchParameters.excludedScenesFromSearch != null )
					{
						foreach( Object obj in searchParameters.excludedScenesFromSearch )
						{
							if( obj == null || obj.Equals( null ) )
								continue;

							if( !obj.IsAsset() )
								continue;

							if( obj.IsFolder() )
							{
								string[] folderContents = AssetDatabase.FindAssets( "t:SceneAsset", new string[] { AssetDatabase.GetAssetPath( obj ) } );
								if( folderContents == null )
									continue;

								for( int i = 0; i < folderContents.Length; i++ )
									excludedScenesPathsSet.Add( AssetDatabase.GUIDToAssetPath( folderContents[i] ) );
							}
							else if( obj is SceneAsset )
								excludedScenesPathsSet.Add( AssetDatabase.GetAssetPath( obj ) );
						}
					}

					foreach( string scenePath in scenesToSearch )
					{
						if( searchParameters.showProgressBar && EditorUtility.DisplayCancelableProgressBar( "Please wait...", "Searching scene: " + scenePath, (float) ++searchProgress / searchTotalProgress ) )
							throw new Exception( "Search aborted" );

						// Search scene for references
						if( string.IsNullOrEmpty( scenePath ) )
							continue;

						if( excludedScenesPathsSet.Contains( scenePath ) )
							continue;

						SearchScene( scenePath, searchResult, initialSceneSetup );
					}
				}

				// Search through all the GameObjects under the DontDestroyOnLoad scene (if exists)
				if( EditorApplication.isPlaying )
				{
					if( searchParameters.showProgressBar && EditorUtility.DisplayCancelableProgressBar( "Please wait...", "Searching scene: DontDestroyOnLoad", 1f ) )
						throw new Exception( "Search aborted" );

					currentSearchResultGroup = new SearchResultGroup( "DontDestroyOnLoad", false );

					GameObject[] rootGameObjects = GetDontDestroyOnLoadObjects();
					for( int i = 0; i < rootGameObjects.Length; i++ )
						SearchGameObjectRecursively( rootGameObjects[i] );

					if( currentSearchResultGroup.NumberOfReferences > 0 )
						searchResult.Add( currentSearchResultGroup );
				}

				InitializeSearchResultNodes( searchResult );

				// Log some c00l stuff to console
				Debug.Log( "Searched " + searchedObjectsCount + " objects in " + ( EditorApplication.timeSinceStartup - searchStartTime ).ToString( "F2" ) + " seconds" );

				return new SearchResult( true, searchResult, initialSceneSetup );
			}
			catch( Exception e )
			{
				Debug.LogException( e );

				try
				{
					InitializeSearchResultNodes( searchResult );
				}
				catch
				{ }

				return new SearchResult( false, searchResult, initialSceneSetup );
			}
			finally
			{
				currentSearchResultGroup = null;
				currentObject = null;

				EditorUtility.ClearProgressBar();
			}
		}

		private void InitializeSearchResultNodes( List<SearchResultGroup> searchResult )
		{
			for( int i = 0; i < searchResult.Count; i++ )
				searchResult[i].InitializeNodes( GetReferenceNode );

			// If there are any empty groups after node initialization, remove those groups
			for( int i = searchResult.Count - 1; i >= 0; i-- )
			{
				if( searchResult[i].NumberOfReferences == 0 )
					searchResult.RemoveAtFast( i );
			}
		}

		// Checks if object is asset or scene object and adds it to the corresponding HashSet(s)
		private void AddSearchedObjectToFilteredSets( Object obj )
		{
			if( obj == null || obj.Equals( null ) )
				return;

			if( obj is SceneAsset )
				return;

			objectsToSearchSet.Add( obj );

			bool isAsset = obj.IsAsset();
			if( isAsset )
			{
				assetsToSearchSet.Add( obj );

				string assetPath = AssetDatabase.GetAssetPath( obj );
				if( !string.IsNullOrEmpty( assetPath ) )
					assetsToSearchPathsSet.Add( assetPath );
			}
			else
			{
				sceneObjectsToSearchSet.Add( obj );

				if( obj is GameObject )
					sceneObjectsToSearchScenesSet.Add( ( (GameObject) obj ).scene.path );
				else if( obj is Component )
					sceneObjectsToSearchScenesSet.Add( ( (Component) obj ).gameObject.scene.path );
			}

			if( obj is GameObject )
			{
				// If searched asset is a GameObject, include its components in the search
				Component[] components = ( (GameObject) obj ).GetComponents<Component>();
				for( int i = 0; i < components.Length; i++ )
				{
					if( components[i] == null || components[i].Equals( null ) )
						continue;

					objectsToSearchSet.Add( components[i] );

					if( isAsset )
						assetsToSearchSet.Add( components[i] );
					else
						sceneObjectsToSearchSet.Add( components[i] );
				}
			}
		}

		// Search a scene for references
		private void SearchScene( string scenePath, List<SearchResultGroup> searchResult, SceneSetup[] initialSceneSetup )
		{
			Scene scene = EditorSceneManager.GetSceneByPath( scenePath );
			if( EditorApplication.isPlaying && !scene.isLoaded )
				return;

			bool canContainSceneObjectReference = scene.isLoaded && ( !EditorSceneManager.preventCrossSceneReferences || sceneObjectsToSearchScenesSet.Contains( scenePath ) );
			if( !canContainSceneObjectReference )
			{
				bool canContainAssetReference = assetsToSearchSet.Count > 0 && ( EditorApplication.isPlaying || AssetHasAnyReference( scenePath ) );
				if( !canContainAssetReference )
					return;
			}

			if( !EditorApplication.isPlaying )
				scene = EditorSceneManager.OpenScene( scenePath, OpenSceneMode.Additive );

			currentSearchResultGroup = new SearchResultGroup( scenePath, true );

			// Search through all the GameObjects in the scene
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			for( int i = 0; i < rootGameObjects.Length; i++ )
				SearchGameObjectRecursively( rootGameObjects[i] );

			// If no references are found in the scene and if the scene is not part of the initial scene setup, close it
			if( currentSearchResultGroup.NumberOfReferences == 0 )
			{
				if( !EditorApplication.isPlaying )
				{
					bool sceneIsOneOfInitials = false;
					for( int i = 0; i < initialSceneSetup.Length; i++ )
					{
						if( initialSceneSetup[i].path == scenePath )
						{
							if( !initialSceneSetup[i].isLoaded )
								EditorSceneManager.CloseScene( scene, false );

							sceneIsOneOfInitials = true;
							break;
						}
					}

					if( !sceneIsOneOfInitials )
						EditorSceneManager.CloseScene( scene, true );
				}
			}
			else
			{
				// Some references are found in this scene, save the results
				searchResult.Add( currentSearchResultGroup );
			}
		}

		// Search a GameObject and its children for references recursively
		private void SearchGameObjectRecursively( GameObject go )
		{
			BeginSearchObject( go );

			Transform tr = go.transform;
			for( int i = 0; i < tr.childCount; i++ )
				SearchGameObjectRecursively( tr.GetChild( i ).gameObject );
		}

		// Begin searching a root object (like a GameObject or an asset)
		private void BeginSearchObject( Object obj )
		{
			if( obj is SceneAsset )
				return;

			currentObject = obj;

			ReferenceNode searchResult = SearchObject( obj );
			if( searchResult != null && currentSearchResultGroup != null )
				currentSearchResultGroup.AddReference( searchResult );
		}

		// Search an object for references
		private ReferenceNode SearchObject( object obj )
		{
			if( obj == null || obj.Equals( null ) )
				return null;

			// Avoid recursion (which leads to stackoverflow exception) using a stack
			if( callStack.ContainsFast( obj ) )
				return null;

			// Hashing does not work well with structs all the time, don't cache search results for structs
			string objHash = null;
			if( !( obj is ValueType ) )
			{
				objHash = obj.Hash();

				// If object was searched before, return the cached result
				ReferenceNode cachedResult;
				if( searchedObjects.TryGetValue( objHash, out cachedResult ) )
					return cachedResult;
			}

			searchedObjectsCount++;

			ReferenceNode result;
			Object unityObject = obj as Object;
			if( unityObject != null )
			{
				// If we hit a searched asset
				if( objectsToSearchSet.Contains( unityObject ) )
				{
					// If we were searching for references of the searched asset that we hit (or a component of it),
					// ignore the hit since the searched asset doesn't have a meaningful reference to itself at the moment
					// If there is a meaningful reference, it will become available when calling the SearchX functions below
					if( currentObject != unityObject && ( !( unityObject is Component ) || currentObject != ( (Component) unityObject ).gameObject ) )
					{
						result = PopReferenceNode( unityObject );
						searchedObjects.Add( objHash, result );

						return result;
					}
				}

				// If the Object is an asset, search it in detail only if its dependencies contain at least one of the searched asset(s)
				if( unityObject.IsAsset() && ( assetsToSearchSet.Count == 0 || !AssetHasAnyReference( AssetDatabase.GetAssetPath( unityObject ) ) ) )
				{
					searchedObjects.Add( objHash, null );
					return null;
				}

				callStack.Add( unityObject );

				// Search the Object in detail
				Func<Object, ReferenceNode> func;
				if( typeToSearchFunction.TryGetValue( unityObject.GetType(), out func ) )
					result = func( unityObject );
				else if( unityObject is Component )
					result = SearchComponent( unityObject );
				else
				{
					result = PopReferenceNode( unityObject );
					SearchFieldsAndPropertiesOf( result );
				}

				callStack.RemoveAt( callStack.Count - 1 );
			}
			else
			{
				// Comply with the recursive search limit
				if( currentDepth >= searchDepthLimit )
					return null;

				callStack.Add( obj );
				currentDepth++;

				result = PopReferenceNode( obj );
				SearchFieldsAndPropertiesOf( result );

				currentDepth--;
				callStack.RemoveAt( callStack.Count - 1 );
			}

			if( result != null && result.NumberOfOutgoingLinks == 0 )
			{
				PoolReferenceNode( result );
				result = null;
			}

			// Cache the search result if we are skimming through a class (not a struct; i.e. objHash != null)
			// and if the object is a UnityEngine.Object (if not, cache the result only if we have actually found something
			// or we are at the root of the search; i.e. currentDepth == 0)
			if( objHash != null && ( result != null || unityObject != null || currentDepth == 0 ) )
				searchedObjects.Add( objHash, result );

			return result;
		}

		private ReferenceNode SearchGameObject( Object unityObject )
		{
			GameObject go = (GameObject) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( go );

			// Check if this GameObject's prefab is one of the selected assets
			if( searchPrefabConnections )
			{
#if UNITY_2018_3_OR_NEWER
				Object prefab = PrefabUtility.GetCorrespondingObjectFromSource( go );
				if( objectsToSearchSet.Contains( prefab ) && go == PrefabUtility.GetNearestPrefabInstanceRoot( go ) )
#else
				Object prefab = PrefabUtility.GetPrefabParent( go );
				if( objectsToSearchSet.Contains( prefab ) && go == PrefabUtility.FindRootGameObjectWithSameParentPrefab( go ) )
#endif
					referenceNode.AddLinkTo( GetReferenceNode( prefab ), "Prefab object" );
			}

			// Search through all the components of the object
			Component[] components = go.GetComponents<Component>();
			for( int i = 0; i < components.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( components[i] ) );

			return referenceNode;
		}

		private ReferenceNode SearchComponent( Object unityObject )
		{
			Component component = (Component) unityObject;

			// Ignore Transform component (no object field to search for)
			if( component is Transform )
				return null;

			ReferenceNode referenceNode = PopReferenceNode( component );

			if( searchMonoBehavioursForScript && component is MonoBehaviour )
			{
				// If a searched asset is script, check if this component is an instance of it
				MonoScript script = MonoScript.FromMonoBehaviour( (MonoBehaviour) component );
				if( objectsToSearchSet.Contains( script ) )
					referenceNode.AddLinkTo( GetReferenceNode( script ) );
			}
			else if( searchRenderers && component is Renderer )
			{
				// If an asset is a shader, texture or material, and this component is a Renderer,
				// search it for references
				Material[] materials = ( (Renderer) component ).sharedMaterials;
				for( int i = 0; i < materials.Length; i++ )
					referenceNode.AddLinkTo( SearchObject( materials[i] ) );
			}
			else if( component is Animation )
			{
				// If this component is an Animation, search its animation clips for references
				foreach( AnimationState anim in (Animation) component )
					referenceNode.AddLinkTo( SearchObject( anim.clip ) );
			}
			else if( component is Animator )
			{
				// If this component is an Animator, search its animation clips for references
				referenceNode.AddLinkTo( SearchObject( ( (Animator) component ).runtimeAnimatorController ) );
			}
#if UNITY_2017_2_OR_NEWER
			else if( component is Tilemap )
			{
				// If this component is a Tilemap, search its tiles for references
				TileBase[] tiles = new TileBase[( (Tilemap) component ).GetUsedTilesCount()];
				( (Tilemap) component ).GetUsedTilesNonAlloc( tiles );

				if( tiles != null )
				{
					for( int i = 0; i < tiles.Length; i++ )
						referenceNode.AddLinkTo( SearchObject( tiles[i] ), "Tile" );
				}
			}
#endif
#if UNITY_2017_1_OR_NEWER
			else if( component is PlayableDirector )
			{
				// If this component is a PlayableDirectory, search its PlayableAsset's scene bindings for references
				PlayableAsset playableAsset = ( (PlayableDirector) component ).playableAsset;
				if( playableAsset != null && !playableAsset.Equals( null ) )
				{
					foreach( PlayableBinding binding in playableAsset.outputs )
						referenceNode.AddLinkTo( SearchObject( ( (PlayableDirector) component ).GetGenericBinding( binding.sourceObject ) ), "Binding: " + binding.streamName );
				}
			}
#endif

			SearchFieldsAndPropertiesOf( referenceNode );
			return referenceNode;
		}

		private ReferenceNode SearchMaterial( Object unityObject )
		{
			Material material = (Material) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( material );

			if( searchMaterialsForShader && objectsToSearchSet.Contains( material.shader ) )
				referenceNode.AddLinkTo( GetReferenceNode( material.shader ), "Shader" );

			if( searchMaterialsForTexture )
			{
				// Search through all the textures attached to this material
				// Credit: http://answers.unity3d.com/answers/1116025/view.html
				Shader shader = material.shader;
				int shaderPropertyCount = ShaderUtil.GetPropertyCount( shader );
				for( int i = 0; i < shaderPropertyCount; i++ )
				{
					if( ShaderUtil.GetPropertyType( shader, i ) == ShaderUtil.ShaderPropertyType.TexEnv )
					{
						string propertyName = ShaderUtil.GetPropertyName( shader, i );
						Texture assignedTexture = material.GetTexture( propertyName );
						if( objectsToSearchSet.Contains( assignedTexture ) )
							referenceNode.AddLinkTo( GetReferenceNode( assignedTexture ), "Shader property: " + propertyName );
					}
				}
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorController( Object unityObject )
		{
			RuntimeAnimatorController controller = (RuntimeAnimatorController) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( controller );

			if( controller is AnimatorController )
			{
				AnimatorControllerLayer[] layers = ( (AnimatorController) controller ).layers;
				for( int i = 0; i < layers.Length; i++ )
				{
					if( objectsToSearchSet.Contains( layers[i].avatarMask ) )
						referenceNode.AddLinkTo( GetReferenceNode( layers[i].avatarMask ), layers[i].name + " Mask" );

					referenceNode.AddLinkTo( SearchObject( layers[i].stateMachine ) );
				}
			}
			else
			{
				AnimationClip[] animClips = controller.animationClips;
				for( int i = 0; i < animClips.Length; i++ )
					referenceNode.AddLinkTo( SearchObject( animClips[i] ) );
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorStateMachine( Object unityObject )
		{
			AnimatorStateMachine animatorStateMachine = (AnimatorStateMachine) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( animatorStateMachine );

			ChildAnimatorStateMachine[] stateMachines = animatorStateMachine.stateMachines;
			for( int i = 0; i < stateMachines.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( stateMachines[i].stateMachine ), "Child State Machine" );

			ChildAnimatorState[] states = animatorStateMachine.states;
			for( int i = 0; i < states.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( states[i].state ) );

			if( searchMonoBehavioursForScript )
			{
				StateMachineBehaviour[] behaviours = animatorStateMachine.behaviours;
				for( int i = 0; i < behaviours.Length; i++ )
				{
					MonoScript script = MonoScript.FromScriptableObject( behaviours[i] );
					if( objectsToSearchSet.Contains( script ) )
						referenceNode.AddLinkTo( GetReferenceNode( script ) );
				}
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorState( Object unityObject )
		{
			AnimatorState animatorState = (AnimatorState) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( animatorState );

			referenceNode.AddLinkTo( SearchObject( animatorState.motion ), "Motion" );

			if( searchMonoBehavioursForScript )
			{
				StateMachineBehaviour[] behaviours = animatorState.behaviours;
				for( int i = 0; i < behaviours.Length; i++ )
				{
					MonoScript script = MonoScript.FromScriptableObject( behaviours[i] );
					if( objectsToSearchSet.Contains( script ) )
						referenceNode.AddLinkTo( GetReferenceNode( script ) );
				}
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorStateTransition( Object unityObject )
		{
			// Don't search AnimatorStateTransition objects, it will just return duplicate results of SearchAnimatorStateMachine
			return PopReferenceNode( unityObject );
		}

		private ReferenceNode SearchBlendTree( Object unityObject )
		{
			BlendTree blendTree = (BlendTree) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( blendTree );

			ChildMotion[] children = blendTree.children;
			for( int i = 0; i < children.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( children[i].motion ), "Motion" );

			return referenceNode;
		}

		private ReferenceNode SearchAnimationClip( Object unityObject )
		{
			AnimationClip clip = (AnimationClip) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( clip );

			// Get all curves from animation clip
			EditorCurveBinding[] objectCurves = AnimationUtility.GetObjectReferenceCurveBindings( clip );
			for( int i = 0; i < objectCurves.Length; i++ )
			{
				// Search through all the keyframes in this curve
				ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve( clip, objectCurves[i] );
				for( int j = 0; j < keyframes.Length; j++ )
					referenceNode.AddLinkTo( SearchObject( keyframes[j].value ), "Keyframe: " + keyframes[j].time );
			}

			// Get all events from animation clip
			AnimationEvent[] events = AnimationUtility.GetAnimationEvents( clip );
			for( int i = 0; i < events.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( events[i].objectReferenceParameter ), "AnimationEvent: " + events[i].time );

			return referenceNode;
		}

#if UNITY_2017_1_OR_NEWER
		private ReferenceNode SearchSpriteAtlas( Object unityObject )
		{
			SpriteAtlas spriteAtlas = (SpriteAtlas) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( spriteAtlas );

			SerializedObject spriteAtlasSO = new SerializedObject( spriteAtlas );
			if( spriteAtlas.isVariant )
			{
				Object masterAtlas = spriteAtlasSO.FindProperty( "m_MasterAtlas" ).objectReferenceValue;
				if( objectsToSearchSet.Contains( masterAtlas ) )
					referenceNode.AddLinkTo( SearchObject( masterAtlas ), "Master Atlas" );
			}

#if UNITY_2018_2_OR_NEWER
			Object[] packables = spriteAtlas.GetPackables();
			if( packables != null )
			{
				for( int i = 0; i < packables.Length; i++ )
					referenceNode.AddLinkTo( SearchObject( packables[i] ), "Packed Texture" );
			}
#else
			SerializedProperty packables = spriteAtlasSO.FindProperty( "m_EditorData.packables" );
			if( packables != null )
			{
				for( int i = 0, length = packables.arraySize; i < length; i++ )
					referenceNode.AddLinkTo( SearchObject( packables.GetArrayElementAtIndex( i ).objectReferenceValue ), "Packed Texture" );
			}
#endif

			return referenceNode;
		}
#endif

		// Search through field and properties of an object for references
		private void SearchFieldsAndPropertiesOf( ReferenceNode referenceNode )
		{
			// Get filtered variables for this object
			VariableGetterHolder[] variables = GetFilteredVariablesForType( referenceNode.nodeObject.GetType() );
			for( int i = 0; i < variables.Length; i++ )
			{
				// When possible, don't search non-serializable variables
				if( searchSerializableVariablesOnly && !variables[i].isSerializable )
					continue;

				try
				{
					object variableValue = variables[i].Get( referenceNode.nodeObject );
					if( variableValue == null )
						continue;

					if( !( variableValue is IEnumerable ) || variableValue is Transform )
						referenceNode.AddLinkTo( SearchObject( variableValue ), ( variables[i].isProperty ? "Property: " : "Variable: " ) + variables[i].name );
					else
					{
						// If the field is IEnumerable (possibly an array or collection), search through members of it
						// Note that Transform IEnumerable (children of the transform) is not iterated
						foreach( object arrayItem in (IEnumerable) variableValue )
							referenceNode.AddLinkTo( SearchObject( arrayItem ), ( variables[i].isProperty ? "Property (IEnumerable): " : "Variable (IEnumerable): " ) + variables[i].name );
					}
				}
				catch( UnassignedReferenceException )
				{ }
				catch( MissingReferenceException )
				{ }
			}
		}

		// Get filtered variables for a type
		private VariableGetterHolder[] GetFilteredVariablesForType( Type type )
		{
			VariableGetterHolder[] result;
			if( typeToVariables.TryGetValue( type, out result ) )
				return result;

			// This is the first time this type of object is seen, filter and cache its variables
			// Variable filtering process:
			// 1- skip Obsolete variables
			// 2- skip primitive types, enums and strings
			// 3- skip common Unity types that can't hold any references (e.g. Vector3, Rect, Color, Quaternion)
			// 
			// P.S. IsPrimitiveUnityType() extension function handles steps 2) and 3)

			validVariables.Clear();

			// Filter the fields
			if( fieldModifiers != ( BindingFlags.Instance | BindingFlags.DeclaredOnly ) )
			{
				Type currType = type;
				while( currType != typeof( object ) )
				{
					FieldInfo[] fields = currType.GetFields( fieldModifiers );
					for( int i = 0; i < fields.Length; i++ )
					{
						// Skip obsolete fields
						if( Attribute.IsDefined( fields[i], typeof( ObsoleteAttribute ) ) )
							continue;

						// Skip primitive types
						if( fields[i].FieldType.IsPrimitiveUnityType() )
							continue;

						// Additional filtering for fields:
						// 1- Ignore "m_RectTransform", "m_CanvasRenderer" and "m_Canvas" fields of Graphic components
						string fieldName = fields[i].Name;
						if( typeof( Graphic ).IsAssignableFrom( currType ) &&
							( fieldName == "m_RectTransform" || fieldName == "m_CanvasRenderer" || fieldName == "m_Canvas" ) )
							continue;

						VariableGetVal getter = fields[i].CreateGetter( type );
						if( getter != null )
							validVariables.Add( new VariableGetterHolder( fields[i], getter, fields[i].IsSerializable() ) );
					}

					currType = currType.BaseType;
				}
			}

			if( propertyModifiers != ( BindingFlags.Instance | BindingFlags.DeclaredOnly ) )
			{
				Type currType = type;
				while( currType != typeof( object ) )
				{
					PropertyInfo[] properties = currType.GetProperties( propertyModifiers );
					for( int i = 0; i < properties.Length; i++ )
					{
						// Skip obsolete properties
						if( Attribute.IsDefined( properties[i], typeof( ObsoleteAttribute ) ) )
							continue;

						// Skip primitive types
						if( properties[i].PropertyType.IsPrimitiveUnityType() )
							continue;

						// No need to check properties with 'override' keyword
						if( properties[i].IsOverridden() )
							continue;

						// Additional filtering for properties:
						// 1- Ignore "gameObject", "transform", "rectTransform" and "attachedRigidbody" properties of Component's to get more useful results
						// 2- Ignore "canvasRenderer" and "canvas" properties of Graphic components
						// 3 & 4- Prevent accessing properties of Unity that instantiate an existing resource (causing memory leak)
						string propertyName = properties[i].Name;
						if( typeof( Component ).IsAssignableFrom( currType ) && ( propertyName == "gameObject" ||
							propertyName == "transform" || propertyName == "attachedRigidbody" || propertyName == "rectTransform" ) )
							continue;
						else if( typeof( Graphic ).IsAssignableFrom( currType ) &&
							( propertyName == "canvasRenderer" || propertyName == "canvas" ) )
							continue;
						else if( typeof( MeshFilter ).IsAssignableFrom( currType ) && propertyName == "mesh" )
							continue;
						else if( typeof( Renderer ).IsAssignableFrom( currType ) &&
							( propertyName == "sharedMaterial" || propertyName == "sharedMaterials" ) )
							continue;
						else if( ( propertyName == "material" || propertyName == "materials" ) &&
							( typeof( Renderer ).IsAssignableFrom( currType ) || typeof( Collider ).IsAssignableFrom( currType ) ||
							typeof( Collider2D ).IsAssignableFrom( currType )
#if !UNITY_2019_3_OR_NEWER
#pragma warning disable 0618
							|| typeof( GUIText ).IsAssignableFrom( currType ) ) )
#pragma warning restore 0618
#endif
							continue;
						else
						{
							VariableGetVal getter = properties[i].CreateGetter();
							if( getter != null )
								validVariables.Add( new VariableGetterHolder( properties[i], getter, properties[i].IsSerializable() ) );
						}
					}

					currType = currType.BaseType;
				}
			}

			result = validVariables.ToArray();

			// Cache the filtered fields
			typeToVariables.Add( type, result );

			return result;
		}

		// Check if the asset at specified path depends on any of the references
		private bool AssetHasAnyReference( string assetPath )
		{
			CacheEntry cacheEntry;
			if( !assetDependencyCache.TryGetValue( assetPath, out cacheEntry ) )
			{
				cacheEntry = new CacheEntry( assetPath );
				assetDependencyCache[assetPath] = cacheEntry;
			}
			else if( !cacheEntry.verified )
				cacheEntry.Verify( assetPath );

			if( cacheEntry.searchResult != CacheEntry.Result.Unknown )
				return cacheEntry.searchResult == CacheEntry.Result.Yes;

			cacheEntry.searchResult = CacheEntry.Result.No;

			string[] dependencies = cacheEntry.dependencies;
			long[] fileSizes = cacheEntry.fileSizes;
			for( int i = 0; i < dependencies.Length; i++ )
			{
				// If a dependency was renamed (which doesn't affect the verified hash, unfortunately),
				// force refresh the asset's dependencies and search it again
				FileInfo assetFile = new FileInfo( dependencies[i] );
				if( !assetFile.Exists || assetFile.Length != fileSizes[i] )
				{
					cacheEntry.Refresh( assetPath );
					cacheEntry.searchResult = CacheEntry.Result.Unknown;

					return AssetHasAnyReference( assetPath );
				}

				if( assetsToSearchPathsSet.Contains( dependencies[i] ) )
				{
					cacheEntry.searchResult = CacheEntry.Result.Yes;
					return true;
				}
			}

			for( int i = 0; i < dependencies.Length; i++ )
			{
				if( AssetHasAnyReference( dependencies[i] ) )
				{
					cacheEntry.searchResult = CacheEntry.Result.Yes;
					return true;
				}
			}

			return false;
		}

		// Get reference node for object
		private ReferenceNode GetReferenceNode( object nodeObject )
		{
			ReferenceNode result;
			string hash = nodeObject.Hash();
			if( !searchedObjects.TryGetValue( hash, out result ) || result == null )
			{
				result = PopReferenceNode( nodeObject );
				searchedObjects[hash] = result;
			}

			return result;
		}

		// Fetch a reference node from pool
		private ReferenceNode PopReferenceNode( object nodeObject )
		{
			ReferenceNode node;
			if( nodesPool.Count == 0 )
				node = new ReferenceNode();
			else
			{
				int index = nodesPool.Count - 1;
				node = nodesPool[index];
				nodesPool.RemoveAt( index );
			}

			node.nodeObject = nodeObject;
			return node;
		}

		// Pool a reference node
		private void PoolReferenceNode( ReferenceNode node )
		{
			node.Clear();
			nodesPool.Add( node );
		}

		// Retrieve the game objects listed under the DontDestroyOnLoad scene
		private GameObject[] GetDontDestroyOnLoadObjects()
		{
			GameObject temp = null;
			try
			{
				temp = new GameObject();
				Object.DontDestroyOnLoad( temp );
				Scene dontDestroyOnLoad = temp.scene;
				Object.DestroyImmediate( temp );
				temp = null;

				return dontDestroyOnLoad.GetRootGameObjects();
			}
			finally
			{
				if( temp != null )
					Object.DestroyImmediate( temp );
			}
		}

		public void SaveCache()
		{
			if( assetDependencyCache == null )
				return;

			try
			{
				using( FileStream stream = new FileStream( CachePath, FileMode.Create ) )
				using( BinaryWriter writer = new BinaryWriter( stream ) )
				{
					writer.Write( assetDependencyCache.Count );

					foreach( var keyValuePair in assetDependencyCache )
					{
						CacheEntry cacheEntry = keyValuePair.Value;
						string[] dependencies = cacheEntry.dependencies;
						long[] fileSizes = cacheEntry.fileSizes;

						writer.Write( keyValuePair.Key );
						writer.Write( cacheEntry.hash );
						writer.Write( dependencies.Length );

						for( int i = 0; i < dependencies.Length; i++ )
						{
							writer.Write( dependencies[i] );
							writer.Write( fileSizes[i] );
						}
					}
				}
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}
		}

		private void LoadCache()
		{
			if( File.Exists( CachePath ) )
			{
				using( FileStream stream = new FileStream( CachePath, FileMode.Open, FileAccess.Read ) )
				using( BinaryReader reader = new BinaryReader( stream ) )
				{
					try
					{
						int cacheSize = reader.ReadInt32();
						assetDependencyCache = new Dictionary<string, CacheEntry>( cacheSize );

						for( int i = 0; i < cacheSize; i++ )
						{
							string assetPath = reader.ReadString();
							string hash = reader.ReadString();

							int dependenciesLength = reader.ReadInt32();
							string[] dependencies = new string[dependenciesLength];
							long[] fileSizes = new long[dependenciesLength];
							for( int j = 0; j < dependenciesLength; j++ )
							{
								dependencies[j] = reader.ReadString();
								fileSizes[j] = reader.ReadInt64();
							}

							assetDependencyCache[assetPath] = new CacheEntry( hash, dependencies, fileSizes );
						}
					}
					catch( Exception e )
					{
						assetDependencyCache = null;
						Debug.LogException( e );
					}
				}
			}

			// Generate cache for all assets for the first time
			if( assetDependencyCache == null )
			{
				assetDependencyCache = new Dictionary<string, CacheEntry>( 1024 * 8 );

				string[] allAssets = AssetDatabase.GetAllAssetPaths();
				if( allAssets.Length > 0 )
				{
					double startTime = EditorApplication.timeSinceStartup;

					try
					{
						for( int i = 0; i < allAssets.Length; i++ )
						{
							if( i % 30 == 0 && EditorUtility.DisplayCancelableProgressBar( "Please wait...", "Generating cache for the first time", (float) i / allAssets.Length ) )
							{
								EditorUtility.ClearProgressBar();
								Debug.LogWarning( "Initial cache generation cancelled, cache will be generated on the fly as more and more assets are searched." );
								break;
							}

							AssetHasAnyReference( allAssets[i] );
						}

						EditorUtility.ClearProgressBar();

						Debug.Log( "Cache generated in " + ( EditorApplication.timeSinceStartup - startTime ).ToString( "F2" ) + " seconds" );
						Debug.Log( "You can always reset the cache by deleting " + Path.GetFullPath( CachePath ) );

						SaveCache();
					}
					catch( Exception e )
					{
						EditorUtility.ClearProgressBar();
						Debug.LogException( e );
					}
				}
			}
		}
	}
}