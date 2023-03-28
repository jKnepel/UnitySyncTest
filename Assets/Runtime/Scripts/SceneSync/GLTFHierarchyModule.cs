using jKnepel.UnitySync.Networking;
using System;
using System.IO;
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

		#region receive data

		public override void OnReceiveData(SyncConnectedClient sender, byte[] data)
		{
			ImportHierarchy(data);
		}

		private async void ImportHierarchy(byte[] hierarchy)
		{
			Action<GameObject, ExceptionDispatchInfo> onLoadComplete = (createdObject, e) =>
			{
				if (createdObject is null)
				{
					return;
				}
				SceneManager.MoveGameObjectToScene(createdObject, _settings.SyncScene);

				SyncManager.AddSyncMessage(new($"GameObject '{createdObject.name}' imported."));
			};

			using MemoryStream memoryStream = new();
			await memoryStream.WriteAsync(hierarchy);
			await DeserializeGameObject(_settings.SyncScene, new ImportOptions(), memoryStream, onLoadComplete);

			Debug.Log($"imported:\n{new GLTFParser().ParseToReadableText(hierarchy)}");

			return;
		}

		private async Task DeserializeGameObject(Scene importScene, ImportOptions importOptions, Stream stream, Action<GameObject, ExceptionDispatchInfo> onLoadComplete)
		{
			GLTFParser.ParseJson(stream, out GLTFRoot gLTFRoot, 0);

			GLTFSceneImporterExtended importer = new(gLTFRoot, stream, importOptions);
			await importer.UpdateOrCreateSceneAsync(GLTFSceneHelper.GetSceneRootComponents(importScene), SyncManager.IsDebug, -1, true, onLoadComplete);
		}

		#endregion

		#region send data

		public async Task<bool> SyncHierarchy(GameObject go)
		{
			GLTFRootGameObject gltf = go.GetRootParent().GetOrAddComponent<GLTFRootGameObject>();
			byte[] data = await gltf.SerializeGameObject(new ExportOptions());

			return Task.Run(() =>
			{
				TaskCompletionSource<bool> t = new();
				SendData(data, wasSend => t.TrySetResult(wasSend));
				return t.Task;
			}).Result;
		}

		#endregion
	}
}
