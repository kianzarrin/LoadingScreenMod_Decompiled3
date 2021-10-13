using System;
using ColossalFramework.UI;
using UnityEngine;

namespace LoadingScreenMod
{
	internal sealed class Text
	{
		internal readonly Vector3 pos;

		internal readonly Source source;

		private readonly float scale;

		private string text = string.Empty;

		internal Mesh mesh = new Mesh();

		private const float baseScale = 0.002083333f;

		internal Vector3 Scale => new Vector3(scale, scale, scale);

		internal Text(Vector3 pos, Source source, float scaleFactor = 1f)
		{
			this.pos = pos;
			this.source = source;
			scale = 0.002083333f * scaleFactor;
		}

		internal void Clear()
		{
			text = string.Empty;
		}

		internal void Dispose()
		{
			if (mesh != null)
			{
				UnityEngine.Object.Destroy(mesh);
			}
			mesh = null;
		}

		internal void UpdateText()
		{
			string check = source.CreateText();
			if (check != null && check != text)
			{
				text = check;
				GenerateMesh();
			}
		}

		private void GenerateMesh()
		{
			UIFont field = Instance<LoadingScreen>.instance.uifont;
			if (field == null)
			{
				return;
			}
			UIFontRenderer parent = field.ObtainRenderer();
			UIRenderData label = UIRenderData.Obtain();
			try
			{
				mesh.Clear();
				parent.defaultColor = Color.white;
				parent.textScale = 1f;
				parent.pixelRatio = 1f;
				parent.processMarkup = true;
				parent.multiLine = true;
				parent.maxSize = new Vector2(Instance<LoadingScreen>.instance.meshWidth, Instance<LoadingScreen>.instance.meshHeight);
				parent.shadow = false;
				parent.Render(text, label);
				mesh.vertices = label.vertices.ToArray();
				mesh.colors32 = label.colors.ToArray();
				mesh.uv = label.uvs.ToArray();
				mesh.triangles = label.triangles.ToArray();
			}
			catch (Exception exception)
			{
				Util.DebugPrint("Cannot generate font mesh");
				Debug.LogException(exception);
			}
			finally
			{
				parent.Dispose();
				label.Dispose();
			}
		}
	}
}
