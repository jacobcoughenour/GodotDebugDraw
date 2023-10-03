using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using Godot;

public partial class DebugDraw : Node3D
{

	private bool _enabled = true;

	public const int TEXT_LINGER_FRAMES = 5;
	public const int LINES_LINGER_FRAMES = 120;
	public static readonly Color TEXT_COLOR = new Color(1, 1, 1);
	public static readonly Color TEXT_BG_COLOR = new Color(0.09f, 0.1f, 0.13f, 0.8f);

	private Node2D _canvas_item;

	private struct DebugEntry<T>
	{
		public DebugEntry(T value, int frame)
		{
			Value = value;
			Frame = frame;
		}
		public T Value
		{
			get;
		}
		public int Frame
		{
			get;
		}
	}

	private System.Collections.Generic.Dictionary<string, DebugEntry<string>> _texts = new();
	private Font _font;
	private ConcurrentQueue<DebugEntry<MeshInstance3D>> _boxes = new();
	private ConcurrentStack<MeshInstance3D> _box_pool = new();
	private Mesh _box_mesh;
	private ConcurrentQueue<DebugEntry<MeshInstance3D>> _lines = new();
	private ConcurrentStack<StandardMaterial3D> _line_material_pool = new();
	private static readonly ConcurrentDictionary<string, int> _metrics = new();

	public const string Path = "/root/DebugDraw";

	public override void _EnterTree()
	{
		base._EnterTree();
		Debug.Assert(GetPath() == Path, $"DebugDraw.Path != {Path}");
	}

	public static DebugDraw Get(Node node)
	{
		return node.GetNodeOrNull<DebugDraw>("/root/DebugDraw");
	}

	public override void _Ready()
	{
		var c = new Control();
		AddChild(c);
		_font = c.GetThemeDefaultFont();
		c.QueueFree();
	}

	public void DrawBox(Transform3D transform, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;

		var mi = GetBox();
		var mat = GetLineMaterial();
		mat.AlbedoColor = color;
		mi.MaterialOverride = mat;
		mi.Transform = transform;
		_boxes.Enqueue(new DebugEntry<MeshInstance3D>(
			mi,
			Engine.GetFramesDrawn() + frames
		));
	}

	public void DrawAABB(Vector3 min, Vector3 extent, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;

		DrawBox(new Transform3D(Basis.Identity, min).Scaled(extent), color, frames);
	}

	public void DrawBox(Vector3 position, Vector3 size, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;

		DrawAABB(position - size * 0.5f, size, color, frames);
	}


	public void DrawLine3D(Vector3 a, Vector3 b, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;

		var mat = GetLineMaterial();
		mat.AlbedoColor = color;
		var g = new ImmediateMesh();
		g.SurfaceBegin(Mesh.PrimitiveType.Lines);
		g.SurfaceSetColor(color);
		g.SurfaceAddVertex(a);
		g.SurfaceAddVertex(b);
		g.SurfaceEnd();

		var mesh = new MeshInstance3D();
		mesh.Mesh = g;
		mesh.MaterialOverride = mat;
		mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		AddChild(mesh);
		_lines.Enqueue(new DebugEntry<MeshInstance3D>(mesh, Engine.GetFramesDrawn() + frames));
	}



	public void DrawPoint3D(Vector3 a, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;

		var mat = GetLineMaterial();
		mat.AlbedoColor = color;
		var g = new ImmediateMesh();
		g.SurfaceBegin(Mesh.PrimitiveType.Lines);
		g.SurfaceSetColor(color);
		var size = 0.06f;
		var sizex = Mathf.Sin(0.25f * Mathf.Pi) * size;
		g.SurfaceAddVertex(a + Vector3.Up * size);
		g.SurfaceAddVertex(a + Vector3.Down * size);
		g.SurfaceAddVertex(a + Vector3.Left * size);
		g.SurfaceAddVertex(a + Vector3.Right * size);
		g.SurfaceAddVertex(a + Vector3.Forward * size);
		g.SurfaceAddVertex(a + Vector3.Back * size);

		g.SurfaceAddVertex(a + Vector3.One * sizex);
		g.SurfaceAddVertex(a + Vector3.One * -sizex);

		g.SurfaceAddVertex(a + new Vector3(-sizex, sizex, -sizex));
		g.SurfaceAddVertex(a + new Vector3(sizex, -sizex, sizex));

		g.SurfaceAddVertex(a + new Vector3(sizex, sizex, -sizex));
		g.SurfaceAddVertex(a + new Vector3(-sizex, -sizex, sizex));

		g.SurfaceAddVertex(a + new Vector3(-sizex, sizex, sizex));
		g.SurfaceAddVertex(a + new Vector3(sizex, -sizex, -sizex));
		g.SurfaceEnd();
		var mesh = new MeshInstance3D
		{
			Mesh = g,
			MaterialOverride = mat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
		};
		AddChild(mesh);
		_lines.Enqueue(new DebugEntry<MeshInstance3D>(mesh, Engine.GetFramesDrawn() + frames));
	}

	public void DrawRay3D(Vector3 origin, Vector3 direction, float length, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;
		DrawLine3D(origin, origin + direction * length, color, frames);
	}

	public void DrawRayCast3D(Vector3 start, Vector3 hit, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;
		DrawLine3D(start, hit, color, frames);
		DrawPoint3D(hit, color, frames);
	}

	public void SetText(string key, string value, int frames = TEXT_LINGER_FRAMES)
	{
		if (!Visible)
			return;
		_texts[key] = new DebugEntry<string>(
			value,
			Engine.GetFramesDrawn() + frames
		);
	}

	public void IncrementMetric(string key, int value = 1)
	{
		if (!Visible)
			return;
		_metrics.AddOrUpdate(key, value, (k, v) => v + value);
	}

	private MeshInstance3D GetBox()
	{
		if (!_box_pool.TryPop(out MeshInstance3D mi))
		{
			mi = new MeshInstance3D();
			if (_box_mesh == null)
				_box_mesh = CreateWirecubeMesh();
			mi.Mesh = _box_mesh;
			AddChild(mi);
		}
		return mi;
	}

	// private void RecycleBox(MeshInstance mi) {
	// 	mi.Hide();
	// 	_box_pool.Push(mi);
	// }

	private StandardMaterial3D GetLineMaterial()
	{
		if (!_line_material_pool.TryPop(out StandardMaterial3D mat))
		{
			mat = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				VertexColorUseAsAlbedo = true
			};
		}
		return mat;
	}

	private void RecycleLineMaterial(StandardMaterial3D mat)
	{
		_line_material_pool.Push(mat);
	}

	public override void _Process(double delta)
	{

		foreach (var (key, value) in _metrics)
		{
			SetText(key, value.ToString());
		}

		if (Input.IsActionJustPressed("debug_toggle"))
		{
			Visible = !Visible;
		};

		int frame = Engine.GetFramesDrawn();

		// remove expired lines
		while (
			_lines.TryPeek(out var _line)
			&& _line.Frame <= frame
			&& _lines.TryDequeue(out _line)
		)
		{
			RecycleLineMaterial((StandardMaterial3D)_line.Value.MaterialOverride);
			_line.Value.QueueFree();
		}

		// remove expired boxes
		while (
			_boxes.TryPeek(out var _box)
			&& _box.Frame <= frame
			&& _boxes.TryDequeue(out _box)
		)
		{
			RecycleLineMaterial((StandardMaterial3D)_box.Value.MaterialOverride);
			_box.Value.QueueFree();
		}

		// remove 1 box from pool
		if (_box_pool.TryPop(out var res))
			res.QueueFree();

		// remove expired text lines
		foreach (var entry in _texts)
		{
			if (entry.Value.Frame <= frame)
			{
				// _texts.TryRemove(entry);
				_texts.Remove(entry.Key);
			}
		}

		if (_canvas_item == null)
		{
			_canvas_item = new Node2D
			{
				GlobalPosition = new Vector2(8, 8)
			};
			_canvas_item.Draw += OnCanvasItemDraw;
			AddChild(_canvas_item);
		}
		// _canvas_item.update();

		_canvas_item.Visible = false;
		_canvas_item.Visible = true;

		_metrics.Clear();
	}

	private const float xpad = 2;
	private const float ypad = 1;
	// private static readonly Vector2 pad = new Vector2(xpad, ypad);

	public void OnCanvasItemDraw()
	{
		var font_offset = new Vector2(xpad, (float)_font.GetAscent() + ypad);
		float line_height = (float)_font.GetHeight() + 2 * ypad;
		var pos = new Vector2();

		foreach (var item in _texts)
		{
			string text = $"{item.Key}: {item.Value.Value}\n";
			Vector2 size = _font.GetStringSize(text);

			_canvas_item.DrawRect(new Rect2(pos, new Vector2(size.X + xpad * 2, line_height)), TEXT_BG_COLOR);
			_canvas_item.DrawString(_font, pos + font_offset, text, modulate: TEXT_COLOR);
			pos.Y += line_height;
		}

	}

	private static readonly Vector3[] _boxVerts = new Vector3[] {
		Vector3.Zero,
		Vector3.Right,
		Vector3.Right + Vector3.Back,
		Vector3.Back,
		Vector3.Up + Vector3.Zero,
		Vector3.Up + Vector3.Right,
		Vector3.Up + Vector3.Right + Vector3.Back,
		Vector3.Up + Vector3.Back,
	};

	private static readonly int[] _boxIndex = new int[] {
		0, 1,
		1, 2,
		2, 3,
		3, 0,

		4, 5,
		5, 6,
		6, 7,
		7, 4,

		0, 4,
		1, 5,
		2, 6,
		3, 7
	};

	private static Godot.Collections.Array _boxArrays;

	private static ArrayMesh CreateWirecubeMesh()
	{
		if (_boxArrays is null)
		{
			_boxArrays = new();
			_boxArrays.Resize((int)Mesh.ArrayType.Max);
			_boxArrays[(int)Mesh.ArrayType.Vertex] = _boxIndex.Select(i => _boxVerts[i]).ToArray().AsSpan();
		}
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, _boxArrays);
		return mesh;
	}
}