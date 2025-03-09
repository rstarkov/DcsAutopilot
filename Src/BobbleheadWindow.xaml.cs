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
    private BobbleSim _sim1, _sim2;

    public BobbleheadWindow() : base(App.Settings.BobbleheadWindow)
    {
        InitializeComponent();
        canvas.Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 1 });
        _sim1 = new BobbleheadForwardSim();
        _sim2 = new BobbleheadSideSim();
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
        {
            _sim1.Step(dtStep);
            _sim2.Step(dtStep);
        }
        _simTimes.Enqueue((DateTime.UtcNow - start).TotalMilliseconds);
        while (_simTimes.Count > 100) _simTimes.Dequeue();
        Title = $"DCS bobblehead: {_simTimes.Average():0.000} ms/frame";

        GL.ClearColor(0, 0, 0, 1.0f);
        GL.ClearStencil(0);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);
        _sim1.Viewport(canvas.ActualWidth, canvas.ActualHeight, 15, 15, canvas.ActualWidth - 15, canvas.ActualWidth - 15);
        _sim1.Paint();
        _sim2.Viewport(canvas.ActualWidth, canvas.ActualHeight, 15, canvas.ActualWidth, canvas.ActualWidth - 15, 2 * canvas.ActualWidth - 15);
        _sim2.Paint();
    }

    public void MoveCockpit(double accFB, double accLR, double accUD, double pitch, double bank)
    {
        _sim1.MoveCockpit(accFB, accLR, accUD, pitch, bank);
        _sim2.MoveCockpit(accFB, accLR, accUD, pitch, bank);
    }
}

class BobbleheadController : FlightControllerBase
{
    public override string Name { get; set; } = "Bobblehead";
    public BobbleheadWindow Window;

    public override ControlData ProcessFrame(FrameData frame)
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
    protected byte[] _colorBorder = new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, }; // 11 elements
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
            filter = new Filter { categoryBits = 1 },
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
        public byte[] Colors;
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

    protected Body addSwing(Vector2 pos, int linkCount, float ropeLength, float ballRadius)
    {
        var attachment = _world.CreateBody(new BodyDef { type = BodyType.Static, position = pos, angle = 0, bullet = false });
        var links = new Body[linkCount];
        var linklen = ropeLength / (links.Length + 1);
        for (int i = 0; i < links.Length; i++)
        {
            links[i] = _world.CreateBody(new BodyDef { type = BodyType.Dynamic, position = new Vector2(pos.X, cm(50) - linklen * (i + 1)), angle = 0, bullet = false, linearDamping = 0.2f });
            links[i].CreateFixture(new FixtureDef { density = 1, friction = 0, restitution = 0.5f, shape = new CircleShape { Radius = cm(0.5f) }, filter = new Filter { categoryBits = 4, maskBits = 1 + 2 } });
        }
        var ballshape = createPolyShape(5, ballRadius, 0.6f, 1f);
        var ball = addPiece(new Vector2(pos.X, cm(50) - linklen * (linkCount + 1) + 0.0154f/*avoid 0,0*/), ballshape);
        ball.SetLinearDampling(0.2f);
        ball.SetAngularDamping(0.2f);
        _world.CreateJoint(new DistanceJointDef { bodyA = attachment, bodyB = links[0], length = linklen });
        for (int i = 1; i < links.Length; i++)
            _world.CreateJoint(new DistanceJointDef { bodyA = links[i - 1], bodyB = links[i], length = linklen });
        _world.CreateJoint(new DistanceJointDef { bodyA = links[^1], bodyB = ball, length = linklen, localAnchorB = ballshape.GetVertices()[0] });
        return attachment;
    }
}

class BobbleheadForwardSim : BobbleSim
{
    protected Body _cockpit;
    protected Body _attachment;
    protected PointD _attachmentPos;

    public BobbleheadForwardSim()
    {
        CreateWorld();
    }

    protected virtual void CreateWorld()
    {
        _world = new World(new Vector2(0, -9.8f * 10));
        _cockpit = addBox();
        for (int i = 0; i < 3; i++)
            addPiece(new Vector2(rnd(cm(-40f), cm(40f)), rnd(cm(-40f), cm(40f))), createPolyShape(Random.Shared.Next(4, 7), rnd(cm(5f), cm(20f)), 0.6f, 1f));
        _attachmentPos = new PointD(cm(0), cm(50));
        _attachment = addSwing(_attachmentPos.ToVector2(), linkCount: 8, ropeLength: cm(40), ballRadius: cm(10));
    }

    public override void MoveCockpit(double accFB, double accLR, double accUD, double pitch, double bank)
    {
        _world.SetGravity((-10 * new PointD(accLR, accUD)).Rotated(bank).ToVector2());
        _cockpit.SetTransform(_cockpit.GetPosition(), -(float)bank);
        _attachment.SetTransform(_attachmentPos.Rotated(bank).ToVector2(), 0);
        // TODO: a large rotation gets applied in one single timestep - spread it over the next frame
    }

    protected override (float xL, float yB, float xR, float yT) GetWorldViewport() => (-cm(50), -cm(50), cm(50), cm(50));

    public override void Paint()
    {
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
                    if (ud.Colors != null)
                        GL.ColorPointer(3, ColorPointerType.UnsignedByte, 0, ud.Colors);
                    else
                        GL.ColorPointer(3, ColorPointerType.UnsignedByte, 0, _colorRainbow);
                    GL.DrawArrays(PrimitiveType.TriangleFan, 0, ud.Vertices.Length / 2);
                }
                if (ud.Colors != null)
                    GL.ColorPointer(3, ColorPointerType.UnsignedByte, 0, ud.Colors);
                else
                    GL.ColorPointer(3, ColorPointerType.UnsignedByte, 0, _colorBorder);
                GL.DrawArrays(PrimitiveType.LineLoop, 0, ud.Vertices.Length / 2);
                GL.PopMatrix();
            }
        }
        for (var joint = _world.GetJointList(); joint != null; joint = joint.GetNext())
        {
            var pA = joint.GetAnchorA;
            var pB = joint.GetAnchorB;
            var ud = (UserData)joint.UserData;
            if (ud?.Colors != null)
                GL.ColorPointer(3, ColorPointerType.UnsignedByte, 0, ud?.Colors);
            else
                GL.ColorPointer(3, ColorPointerType.UnsignedByte, 0, _colorBorder);
            GL.VertexPointer(2, VertexPointerType.Float, 0, new float[] { pA.X, pA.Y, pB.X, pB.Y });
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }
        GL.PopMatrix();
    }
}

class BobbleheadSideSim : BobbleheadForwardSim
{
    private Body _headAtt1, _headAtt2;
    private PointD _headAtt1Pos, _headAtt2Pos;

    protected override void CreateWorld()
    {
        _world = new World(new Vector2(0, -9.8f * 10));
        _cockpit = addBox();
        for (int i = 0; i < 1; i++)
            addPiece(new Vector2(rnd(cm(-40f), cm(40f)), rnd(cm(-40f), cm(40f))), createPolyShape(Random.Shared.Next(4, 7), rnd(cm(8f), cm(14f)), 0.6f, 1f));
        _attachmentPos = new PointD(cm(15), cm(50));
        _attachment = addSwing(_attachmentPos.ToVector2(), linkCount: 7, ropeLength: cm(30), ballRadius: cm(5));
        addHead(true, cm(-10));
    }

    private void addHead(bool middleLink, float att1x)
    {
        var att2x = att1x + cm(15);
        _headAtt1Pos = new PointD(att1x, cm(-50));
        _headAtt2Pos = new PointD(att2x, cm(-50));
        _headAtt1 = _world.CreateBody(new BodyDef { type = BodyType.Static, position = _headAtt1Pos.ToVector2(), angle = 0, bullet = false });
        _headAtt2 = _world.CreateBody(new BodyDef { type = BodyType.Static, position = _headAtt2Pos.ToVector2(), angle = 0, bullet = false });
        var head = _world.CreateBody(new BodyDef
        {
            type = BodyType.Dynamic,
            position = new Vector2(att1x, cm(-50 + (middleLink ? 30 : 15))),
            allowSleep = false,
            awake = true,
            bullet = true,
            linearDamping = 0.5f,
            angularDamping = 0.2f,
            gravityScale = 1,
        });
        head.CreateFixture(new FixtureDef
        {
            friction = 0.1f,
            restitution = 0.5f,
            density = 1,
            isSensor = false,
            shape = new PolygonShape(new Vector2(0, 0), new Vector2(cm(15), 0), new Vector2(cm(20), cm(12.5f)), new Vector2(cm(15), cm(25)), new Vector2(cm(0), cm(25)), new Vector2(cm(-5), cm(12.5f))),
        });
        head.SetUserData(new UserData
        {
            Vertices = new[] { new Vector2(0, 0), new Vector2(cm(15), 0),
                /*mouth*/new Vector2(cm(18), cm(7)), new Vector2(cm(15), cm(7)), new Vector2(cm(18.5f), cm(8.5f)),
                /*nose*/new Vector2(cm(18), cm(12.5f)), new Vector2(cm(23), cm(12.5f)), new Vector2(cm(17), cm(18f)),
                new Vector2(cm(15), cm(25)), new Vector2(cm(0), cm(25)), new Vector2(cm(-5), cm(12.5f)) }.SelectMany(v => new[] { v.X, v.Y }).ToArray(),
            Colors = new byte[] { 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, }, // 11 elements
            Filled = false,
        });
        DistanceJointDef neckJ(Body a, Body b, float length, DistanceJointDef jd)
        {
            jd.bodyA = a;
            jd.bodyB = b;
            jd.length = length;
            jd.stiffness = 5000f; // 5k: buckle at 2g but nice lean; 15k: buckle at 6g but almost no lean
            jd.damping = 100f;
            jd.UserData = new UserData { Colors = new byte[] { 90, 90, 90, 90, 90, 90 } };
            return jd;
        }
        if (middleLink)
        {
            var hlink1 = _world.CreateBody(new BodyDef { type = BodyType.Dynamic, position = new Vector2(att1x, cm(-35)), angle = 0, bullet = false, linearDamping = 0.2f });
            hlink1.CreateFixture(new FixtureDef { density = 1, friction = 0, restitution = 0.5f, shape = new CircleShape { Radius = cm(0.5f) }, filter = new Filter { categoryBits = 4, maskBits = 2 } });
            var hlink2 = _world.CreateBody(new BodyDef { type = BodyType.Dynamic, position = new Vector2(att2x, cm(-35)), angle = 0, bullet = false, linearDamping = 0.2f });
            hlink2.CreateFixture(new FixtureDef { density = 1, friction = 0, restitution = 0.5f, shape = new CircleShape { Radius = cm(0.5f) }, filter = new Filter { categoryBits = 4, maskBits = 2 } });
            _world.CreateJoint(neckJ(_headAtt1, hlink1, cm(15), new DistanceJointDef { }));
            _world.CreateJoint(neckJ(_headAtt1, hlink2, cm(MathF.Sqrt(2 * 15 * 15)), new DistanceJointDef { }));
            _world.CreateJoint(neckJ(_headAtt2, hlink1, cm(MathF.Sqrt(2 * 15 * 15)), new DistanceJointDef { }));
            _world.CreateJoint(neckJ(_headAtt2, hlink2, cm(15), new DistanceJointDef { }));
            _world.CreateJoint(neckJ(hlink1, hlink2, cm(15), new DistanceJointDef { }));
            _world.CreateJoint(neckJ(hlink1, head, cm(15), new DistanceJointDef { localAnchorB = new Vector2(0, 0) }));
            _world.CreateJoint(neckJ(hlink1, head, cm(MathF.Sqrt(2 * 15 * 15)), new DistanceJointDef { localAnchorB = new Vector2(cm(15), 0) }));
            _world.CreateJoint(neckJ(hlink2, head, cm(MathF.Sqrt(2 * 15 * 15)), new DistanceJointDef { localAnchorB = new Vector2(0, 0) }));
            _world.CreateJoint(neckJ(hlink2, head, cm(15), new DistanceJointDef { localAnchorB = new Vector2(cm(15), 0) }));
        }
        else
        {
            _world.CreateJoint(neckJ(_headAtt1, head, cm(15), new DistanceJointDef { localAnchorB = new Vector2(0, 0) }));
            _world.CreateJoint(neckJ(_headAtt1, head, cm(MathF.Sqrt(2 * 15 * 15)), new DistanceJointDef { localAnchorB = new Vector2(cm(15), 0) }));
            _world.CreateJoint(neckJ(_headAtt2, head, cm(MathF.Sqrt(2 * 15 * 15)), new DistanceJointDef { localAnchorB = new Vector2(0, 0) }));
            _world.CreateJoint(neckJ(_headAtt2, head, cm(15), new DistanceJointDef { localAnchorB = new Vector2(cm(15), 0) }));
        }
    }

    public override void MoveCockpit(double accFB, double accLR, double accUD, double pitch, double bank)
    {
        _world.SetGravity((-10 * new PointD(accFB, accUD)).Rotated(-pitch).ToVector2());
        _cockpit.SetTransform(_cockpit.GetPosition(), (float)pitch);
        _attachment.SetTransform(_attachmentPos.Rotated(-pitch).ToVector2(), 0);
        _headAtt1.SetTransform(_headAtt1Pos.Rotated(-pitch).ToVector2(), 0);
        _headAtt2.SetTransform(_headAtt2Pos.Rotated(-pitch).ToVector2(), 0);
    }
}
