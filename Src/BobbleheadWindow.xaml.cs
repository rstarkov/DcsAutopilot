using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Fixtures;
using Box2D.NetStandard.Dynamics.Joints.Distance;
using Box2D.NetStandard.Dynamics.World;
using OpenTK.Graphics.OpenGL;
using OpenTK.Wpf;
using RT.Util.ExtensionMethods;
using RT.Util.Forms;
using RT.Util.Geometry;

namespace DcsAutopilot;

public partial class BobbleheadWindow : ManagedWindow
{
    private BobbleSim _sim;

    public BobbleheadWindow() : base(App.Settings.BobbleheadWindow)
    {
        InitializeComponent();
        canvas.RegisterToEventsDirectly = false; // workaround for StackOverflow in https://github.com/opentk/GLWpfControl/issues/82
        canvas.Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 1 });
        _sim = new BobbleheadSideSim();
    }

    private Queue<double> _simTimes = new();

    private void canvas_Render(TimeSpan dt)
    {
        if (dt.TotalSeconds > 0.1)
            return;
        const double dt_tgt = 0.001; // maximum sim dt
        int steps = (int)Math.Ceiling(dt.TotalSeconds / dt_tgt);
        var dtStep = (float)(dt.TotalSeconds / steps);
        var start = DateTime.UtcNow;
        for (int i = 0; i < steps; i++)
            _sim.Step(dtStep);
        _simTimes.Enqueue((DateTime.UtcNow - start).TotalMilliseconds);
        while (_simTimes.Count > 100) _simTimes.Dequeue();
        Title = $"DCS bobblehead: {_simTimes.Average():0.000} ms/frame";

        _sim.Viewport(canvas.ActualWidth, canvas.ActualHeight, 15, 15, canvas.ActualHeight - 30, canvas.ActualHeight - 30);
        _sim.Paint();
    }

    public void MoveCockpit(double accFB, double accLR, double accUD, double pitch, double bank)
    {
        _sim.MoveCockpit(accFB, accLR, accUD, pitch, bank);
    }
}

class BobbleheadController : IFlightController
{
    public bool Enabled { get; set; }
    public string Status => "";
    public void NewSession(BulkData bulk) { }
    public void ProcessBulkUpdate(BulkData bulk) { }
    public BobbleheadWindow Window;

    public ControlData ProcessFrame(FrameData frame)
    {
        Window?.MoveCockpit(frame.AccX * 9.81, frame.AccZ * 9.81, frame.AccY * 9.81, frame.Pitch.ToRad(), frame.Bank.ToRad());
        return null;
    }
}

abstract class BobbleSim
{
    protected World _world;
    public abstract void MoveCockpit(double accFB, double accLR, double accUD, double pitch, double bank);
    public abstract void Paint();
    public virtual void Step(double dt)
    {
        _world.Step((float)dt, 8, 8);
    }
    protected byte[] _colorBorder = new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, }; // 9 elements
    protected byte[] _colorRainbow = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255 }; // 9 elements

    protected static float cm(float cm)
    {
        // fixed Box2D constants: gap = 0.005 (x4) units; max speed = 200 units / time unit
        // with m/s units max speed is fine but gap is 2cm, very visible
        // with 1 unit = 10cm the gap is just about acceptable, max speed is 20m/s. At 10g max speed reached in 1m is 14m/s
        return cm / 10f;
    }

    protected Body addBox()
    {
        var body = _world.CreateBody(new BodyDef { type = BodyType.Static, position = new Vector2(0, 0), angle = 0, bullet = false });
        var shape = new ChainShape();
        shape.CreateLoop(new[] { new Vector2(cm(-50), cm(-50)), new Vector2(cm(-50), cm(50)), new Vector2(cm(50), cm(50)), new Vector2(cm(50), cm(-50)) });
        body.CreateFixture(new FixtureDef { friction = 0.1f, restitution = 0.5f, density = 0, shape = shape, filter = new Filter { categoryBits = 2 } });
        body.SetUserData(new UserData
        {
            Vertices = new float[] { cm(-50), cm(-50), cm(-50), cm(50), cm(50), cm(50), cm(50), cm(-50) },
            Filled = false,
        });
        return body;
    }

    protected Body addPiece(Vector2 pos, PolygonShape shape)
    {
        var body = _world.CreateBody(new BodyDef
        {
            type = BodyType.Dynamic,
            position = pos,
            angle = 0,
            linearVelocity = Vector2.Zero,
            angularVelocity = rnd(-1.5f, 1.5f),
            allowSleep = false,
            awake = true,
            fixedRotation = false,
            bullet = true,
            //linearDamping = 0.1f,
            //angularDamping = 0.1f,
            gravityScale = 1,
        });
        body.CreateFixture(new FixtureDef
        {
            friction = 0.1f,
            restitution = 0.5f,
            density = 1,
            isSensor = false,
            shape = shape,
        });
        body.SetUserData(new UserData
        {
            Vertices = shape.GetVertices().SelectMany(v => new[] { v.X, v.Y }).ToArray(),
            Filled = true,
        });
        return body;
    }

    protected class UserData
    {
        public float[] Vertices;
        public bool Filled;
    }

    protected static PolygonShape createPolyShape(int vertices, float radius, float maxRndShortening, float maxRndAngle)
    {
        var angles = new float[vertices];
        for (int i = 0; i < vertices; i++)
            angles[i] = i * 6.283f / vertices + rnd(-maxRndAngle / 2, maxRndAngle / 2);
        Array.Sort(angles);
        return new PolygonShape((from a in angles let rad = radius * rnd(maxRndShortening, 1f) select new Vector2(rad * MathF.Cos(a), rad * MathF.Sin(a))).ToArray());
    }

    protected static float rnd(float min, float max) => (float)Random.Shared.NextDouble(min, max);

    protected abstract (float xL, float yB, float xR, float yT) GetWorldViewport();

    public void Viewport(double width, double height, double xL, double yT, double xR, double yB)
    {
        GL.MatrixMode(MatrixMode.Projection);
        GL.LoadIdentity();
        GL.MatrixMode(MatrixMode.Modelview);
        GL.LoadIdentity();
        var w = GetWorldViewport();
        GL.Ortho(Util.Linterp(xL, xR, w.xL, w.xR, 0), Util.Linterp(xL, xR, w.xL, w.xR, width), Util.Linterp(yT, yB, w.yT, w.yB, height), Util.Linterp(yT, yB, w.yT, w.yB, 0), -1.0, 1.0);
    }
}

class BobbleheadForwardSim : BobbleSim
{
    protected Body _cockpit;
    protected Body _attachment;

    public BobbleheadForwardSim()
    {
        _world = new World(new Vector2(0, -9.8f * 10));
        _cockpit = addBox();
        for (int i = 0; i < 3; i++)
            addPiece(new Vector2(rnd(cm(-40f), cm(40f)), rnd(cm(-40f), cm(40f))), createPolyShape(Random.Shared.Next(4, 7), rnd(cm(5f), cm(20f)), 0.6f, 1f));
        _attachment = _world.CreateBody(new BodyDef { type = BodyType.Static, position = new Vector2(0, cm(50)), angle = 0, bullet = false });
        var links = new Body[8];
        var linklen = cm(40) / (links.Length + 1);
        for (int i = 0; i < links.Length; i++)
        {
            links[i] = _world.CreateBody(new BodyDef { type = BodyType.Dynamic, position = new Vector2(0, cm(50) - linklen * (i + 1)), angle = 0, bullet = false, linearDamping = 0.2f });
            links[i].CreateFixture(new FixtureDef { density = 1, friction = 0, restitution = 0.5f, shape = new CircleShape { Radius = cm(0.5f) }, filter = new Filter { categoryBits = 4, maskBits = 1 + 2 } });
        }
        var ballshape = createPolyShape(5, cm(10), 0.6f, 1f);
        var ball = addPiece(new Vector2(0, cm(1)), ballshape);
        ball.SetLinearDampling(0.2f);
        ball.SetAngularDamping(0.2f);
        _world.CreateJoint(new DistanceJointDef { bodyA = _attachment, bodyB = links[0], length = linklen });
        for (int i = 1; i < links.Length; i++)
            _world.CreateJoint(new DistanceJointDef { bodyA = links[i - 1], bodyB = links[i], length = linklen });
        _world.CreateJoint(new DistanceJointDef { bodyA = links[^1], bodyB = ball, length = linklen, localAnchorB = ballshape.GetVertices()[0] });
    }

    public override void MoveCockpit(double accFB, double accLR, double accUD, double pitch, double bank)
    {
        var accVector = new PointD(-10 * (float)accLR, -10 * (float)accUD).Rotated(bank);
        _world.SetGravity(new Vector2((float)accVector.X, (float)accVector.Y));
        _cockpit.SetTransform(_cockpit.GetPosition(), -(float)bank);
        _attachment.SetTransform(new Vector2(cm(50) * (float)Math.Sin(bank), cm(50) * (float)Math.Cos(bank)), 0);
    }

    protected override (float xL, float yB, float xR, float yT) GetWorldViewport() => (-cm(50), -cm(50), cm(50), cm(50));

    public override void Paint()
    {
        GL.ClearColor(0, 0, 0, 1.0f);
        GL.ClearStencil(0);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);

        GL.EnableClientState(ArrayCap.VertexArray);
        GL.EnableClientState(ArrayCap.ColorArray);

        GL.MatrixMode(MatrixMode.Modelview);
        GL.PushMatrix();
        GL.Rotate(-_cockpit.GetAngle() / Math.PI * 180, 0, 0, 1);
        for (var body = _world.GetBodyList(); body != null; body = body.GetNext())
        {
            var ud = body.GetUserData<UserData>();
            var pos = body.GetPosition();
            if (ud != null)
            {
                GL.MatrixMode(MatrixMode.Modelview);
                GL.PushMatrix();
                GL.Translate(pos.X, pos.Y, 0);
                GL.Rotate(body.GetAngle() / Math.PI * 180, 0, 0, 1);
                GL.VertexPointer(2, VertexPointerType.Float, 0, ud.Vertices);
                if (ud.Filled)
                {
                    GL.ColorPointer(3, ColorPointerType.UnsignedByte, 0, _colorRainbow);
                    GL.DrawArrays(PrimitiveType.TriangleFan, 0, ud.Vertices.Length / 2);
                }
                GL.ColorPointer(3, ColorPointerType.UnsignedByte, 0, _colorBorder);
                GL.DrawArrays(PrimitiveType.LineLoop, 0, ud.Vertices.Length / 2);
                GL.PopMatrix();
            }
        }
        GL.ColorPointer(3, ColorPointerType.UnsignedByte, 0, _colorBorder);
        for (var joint = _world.GetJointList(); joint != null; joint = joint.GetNext())
        {
            var pA = joint.GetAnchorA;
            var pB = joint.GetAnchorB;
            GL.VertexPointer(2, VertexPointerType.Float, 0, new float[] { pA.X, pA.Y, pB.X, pB.Y });
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }
        GL.PopMatrix();
    }
}

class BobbleheadSideSim : BobbleheadForwardSim
{
    public override void MoveCockpit(double accFB, double accLR, double accUD, double pitch, double bank)
    {
        var accVector = new PointD(-10 * (float)accFB, -10 * (float)accUD).Rotated(pitch);
        _world.SetGravity(new Vector2((float)accVector.X, (float)accVector.Y));
        _cockpit.SetTransform(_cockpit.GetPosition(), -(float)pitch);
        _attachment.SetTransform(new Vector2(cm(50) * (float)Math.Sin(pitch), cm(50) * (float)Math.Cos(pitch)), 0);
    }
}
