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

	/// <summary>
	/// The path to the DebugDraw node singleton in the tree.
	/// </summary>
	public const string Path = "/root/DebugDraw";

	/// <summary>
	/// Get the DebugDraw singleton node.
	/// </summary>
	/// <param name="node">Your node in the tree so it can get a reference to the scene tree.</param>
	public static DebugDraw Get(Node node)
	{
		return node.GetNodeOrNull<DebugDraw>("/root/DebugDraw");
	}

	/// <summary>
	/// Draws a wire-frame box using the given transform.
	/// The box starts at 0,0,0 and extends to 1,1,1 with the lines aligned to
	/// the axes of the transform instead of the world axes.
	/// </summary>
	/// <param name="transform">Transform to draw box within.</param>
	/// <param name="color">Color to use for the lines.</param>
	/// <param name="frames">How many frames the box stays on screen.</param>
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

	/// <summary>
	/// Draws an wire-frame axis aligned bounding box.
	/// </summary>
	/// <param name="min">The min value of the bounding box.</param>
	/// <param name="extent">The scale of the bounding box.</param>
	/// <param name="color">Color to use for the lines.</param>
	/// <param name="frames">How many frames the box stays on screen.</param>
	public void DrawAABB(Vector3 min, Vector3 extent, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;
		DrawBox(new Transform3D(Basis.Identity, min).Scaled(extent), color, frames);
	}

	/// <summary>
	/// Draws an wire-frame axis aligned box.
	/// </summary>
	/// <param name="position">Center position of the box.</param>
	/// <param name="size">Size of the box.</param>
	/// <param name="color">Color to use for the lines.</param>
	/// <param name="frames">How many frames the box stays on screen.</param>
	public void DrawBox(Vector3 position, Vector3 size, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;
		DrawAABB(position - size * 0.5f, size, color, frames);
	}

	/// <summary>
	/// Draws a line between two points.
	/// </summary>
	/// <param name="a">First point.</param>
	/// <param name="b">Second point.</param>
	/// <param name="color">Color to use for the lines.</param>
	/// <param name="frames">How many frames the box stays on screen.</param>
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

	/// <summary>
	/// Draws a point at the given position. The point is just made of a bunch of lines.
	/// </summary>
	/// <param name="position">Position of the point.</param>
	/// <param name="color">Color to use for the lines.</param>
	/// <param name="frames">How many frames the box stays on screen.</param>
	public void DrawPoint3D(Vector3 position, Color color, int frames = LINES_LINGER_FRAMES)
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
		g.SurfaceAddVertex(position + Vector3.Up * size);
		g.SurfaceAddVertex(position + Vector3.Down * size);
		g.SurfaceAddVertex(position + Vector3.Left * size);
		g.SurfaceAddVertex(position + Vector3.Right * size);
		g.SurfaceAddVertex(position + Vector3.Forward * size);
		g.SurfaceAddVertex(position + Vector3.Back * size);

		g.SurfaceAddVertex(position + Vector3.One * sizex);
		g.SurfaceAddVertex(position + Vector3.One * -sizex);

		g.SurfaceAddVertex(position + new Vector3(-sizex, sizex, -sizex));
		g.SurfaceAddVertex(position + new Vector3(sizex, -sizex, sizex));

		g.SurfaceAddVertex(position + new Vector3(sizex, sizex, -sizex));
		g.SurfaceAddVertex(position + new Vector3(-sizex, -sizex, sizex));

		g.SurfaceAddVertex(position + new Vector3(-sizex, sizex, sizex));
		g.SurfaceAddVertex(position + new Vector3(sizex, -sizex, -sizex));
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

	/// <summary>
	/// Draws a ray from an origin point extending in a direction for a given length.
	/// </summary>
	/// <param name="origin">Origin point of the ray.</param>
	/// <param name="direction">Direction of the ray.</param>
	/// <param name="length">Length of the ray.</param>
	/// <param name="color">Color to use for the lines.</param>
	/// <param name="frames">How many frames the box stays on screen.</param>
	public void DrawRay3D(Vector3 origin, Vector3 direction, float length, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;
		DrawLine3D(origin, origin + direction.Normalized() * length, color, frames);
	}

	/// <summary>
	/// Draws a ray and a hit point.
	/// </summary>
	/// <param name="start">Starting point of the ray cast.</param>
	/// <param name="hit">Hit point of the ray cast.</param>
	/// <param name="color">Color to use for the lines.</param>
	/// <param name="frames">How many frames the box stays on screen.</param>
	public void DrawRayCast3D(Vector3 start, Vector3 hit, Color color, int frames = LINES_LINGER_FRAMES)
	{
		if (!Visible)
			return;
		DrawLine3D(start, hit, color, frames);
		DrawPoint3D(hit, color, frames);
	}

	/// <summary>
	/// Sets key/value text to be displayed on screen for a given number of frames.
	/// Calling multiple times will just update the value and reset the frame counter.
	/// </summary>
	/// <param name="key">The key to display.</param>
	/// <param name="value">The value to display.</param>
	/// <param name="frames">How many frames the text stays on screen.</param>
	public void SetText(string key, string value, int frames = TEXT_LINGER_FRAMES)
	{
		if (!Visible)
			return;
		_texts[key] = new DebugEntry<string>(
			value,
			Engine.GetFramesDrawn() + frames
		);
	}

	/// <summary>
	/// Similar to SetText() but will increment the value by 1 or a given amount.
	/// The metric is rendered in the next frame then cleared.
	/// Use for when you want to count how many times something happens per frame.
	/// </summary>
	/// <param name="key">The key to display.</param>
	/// <param name="value">The value to increment by.</param>
	public void IncrementMetric(string key, int value = 1)
	{
		if (!Visible)
			return;
		_metrics.AddOrUpdate(key, value, (k, v) => v + value);
	}

	public override void _EnterTree()
	{
		base._EnterTree();
		Debug.Assert(GetPath() == Path, $"DebugDraw.Path != {Path}");
	}

	public override void _Ready()
	{
		var c = new Control();
		AddChild(c);
		_font = c.GetThemeDefaultFont();
		c.QueueFree();
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