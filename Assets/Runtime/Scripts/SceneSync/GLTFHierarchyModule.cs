using jKnepel.UnitySync.Networking;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using GLTF;
using GLTF.Schema;
using UnityGLTF;
using Toph.GLTFSerializer.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace jKnepel.UnitySync
{
	public class GLTFHierarchyModule : SyncReliableModule
	{
		private static readonly SyncSettings _settings = SyncManager.GetSyncSettings();

		private readonly ConcurrentDictionary<int, BufferedHierarchy> _bufferedHierarchies = new();
		private int _gltfHierarchyDelay = _settings.GLTFHierarchyDelay;

		private Scene _syncScene;

		#region lifecycle

		public GLTFHierarchyModule(Scene syncScene)
		{
			_syncScene = syncScene;
		}

		public override void Dispose()
		{
			_bufferedHierarchies.Clear();
			base.Dispose();
		}

		public override void Update()
		{
			_gltfHierarchyDelay++;
			if (_gltfHierarchyDelay >= _settings.GLTFHierarchyDelay && _bufferedHierarchies.Count > 0)
			{
				// TODO : order of send is random, consider using different collection?
				int key = _bufferedHierarchies.ElementAt(0).Key;
				_bufferedHierarchies.TryRemove(key, out BufferedHierarchy hier);
				SyncHierarchy(hier);
				_gltfHierarchyDelay = 0;
			}
		}

		#endregion

		#region receive data

		public override void OnReceiveData(SyncConnectedClient sender, byte[] data)
		{
			Toph.GLTFSerializer.GLTFImporter.ImportHierarchy(data, _syncScene, new ImportOptions(), (t, e) => { });
		}

		/*
		private async void ImportHierarchy(byte[] hierarchy)
		{
			Action<GameObject, ExceptionDispatchInfo> onLoadComplete = (createdObject, e) =>
			{
				if (createdObject is null)
				{
					return;
				}
				SceneManager.MoveGameObjectToScene(createdObject, _syncScene);

				SyncManager.AddSyncMessage(new($"GameObject '{createdObject.name}' imported."));
			};

			using MemoryStream memoryStream = new();
			await memoryStream.WriteAsync(hierarchy);
			await DeserializeGameObject(_syncScene, new ImportOptions(), memoryStream, onLoadComplete);

			Debug.Log($"imported:\n{new GLTFParser().ParseToReadableText(hierarchy)}");

			return;
		}

		private async Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream, Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
		{
			GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

			GLTFSceneImporterExtended importer = new(gLTFRoot, stream, importOptions);
			await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), SyncManager.IsDebug, -1, true, onLoadComplete);
		}
		*/

		#endregion

		#region send data

		public void SyncHierarchy(GameObject go, Action<bool> onDataSend)
		{
			GameObject root = go.GetRootParent();
			int instanceID = root.GetInstanceID();

			if (go.scene != _syncScene)
			{
				Debug.LogError($"The Hierarchy {root.name} ({instanceID}) is not part of the synced Scene!");
				onDataSend?.Invoke(false);
				return;
			}

			if (_bufferedHierarchies.TryGetValue(instanceID, out BufferedHierarchy hierarchy))
			{
				hierarchy.OnDataSend = (Action<bool>)Delegate.Combine(hierarchy.OnDataSend, onDataSend);
			}
			else
			{
				_bufferedHierarchies.TryAdd(instanceID, new(instanceID, root, onDataSend));
			}
		}

		private async void SyncHierarchy(BufferedHierarchy hier)
		{
			GLTFRootGameObject root = hier.GameObject.GetOrAddComponent<GLTFRootGameObject>();
			byte[] data = await root.ExportHierarchy(new ExportOptions());
			SendData(data, wasSend => hier.OnDataSend?.Invoke(wasSend));
		}

		#endregion

		private class BufferedHierarchy
		{
			public readonly int InstanceID;
			public readonly GameObject GameObject;
			public Action<bool> OnDataSend;

			public BufferedHierarchy(int instanceId, GameObject gameObject, Action<bool> onDataSend)
			{
				InstanceID = instanceId;
				GameObject = gameObject;
				OnDataSend = onDataSend;
			}
		}
	}
}
