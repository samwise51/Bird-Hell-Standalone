using BirdHell.Entities;
using BirdHell.Diagnostics;
using BirdHell.Presentation;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Geometry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace BirdHell.Core;

public sealed class BirdHellGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly CaveGenerationProfileSelection _caveProfile;
    private readonly FlightRecorderOptions _flightRecorderOptions;
    private readonly FrameStallDiagnosticOptions _stallDiagnosticOptions;
    private readonly Camera2D _camera = new();
    private readonly DebugPresentationSettings _debug = new();
    private readonly WorldAnimationState _animations = new();
    private readonly CloudAtmosphereState _atmosphere = new();
    private BasicEffect? _effect;
    private SpritePresentation? _spritePresentation;
    private EnvironmentPresentation? _environmentPresentation;
    private GameplayAudio? _audio;
    private MainScreenRun? _run;
    private VertexPositionColor[] _terrainVertices = [];
    private int[] _triangleIndices = [];
    private VertexPositionColor[] _outlineVertices = [];
    private KeyboardState _previousKeyboardState;
    private readonly GameplayInputAdapter _gameplayInput = new();
    private GamePadState[] _gamePadStates = [];
    private StallSectionTiming[] _gamePadPollingTimings = [];
    private SimulationStepTimings[] _simulationStepTimings = [];
    private EnemyNavigationStepTimings[] _enemyNavigationStepTimings = [];
    private StallSectionTiming[] _sceneryLayerTimings = [];
    private RenderBatchTimings[] _sceneryBatchTimings = [];
    private double _simulationAccumulator;
    private int _displayedHealth = -1;
    private int _displayedEnemyCount = -1;
    private bool _displayedAllEnemiesDead;
    private float _navigationDiagnosticTitleTimer;
    private FlightRecorder? _flightRecorder;
    private FrameStallDiagnostics? _stallDiagnostics;

    private const float SimulationStep = 0.02f;
    private const double MaximumFrameTime = 0.1;
    private const int MaximumInstrumentedCatchUpSteps = 8;
    private const int SceneryLayerCount = 5;

    public BirdHellGame(
        CaveGenerationProfileSelection? caveProfile = null,
        FlightRecorderOptions? flightRecorderOptions = null,
        FrameStallDiagnosticOptions? stallDiagnosticOptions = null)
    {
        _caveProfile = caveProfile ?? CaveGenerationProfiles.Default;
        _flightRecorderOptions = flightRecorderOptions ?? new FlightRecorderOptions();
        _stallDiagnosticOptions = stallDiagnosticOptions ?? new FrameStallDiagnosticOptions();
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };

        Content.RootDirectory = "Content";
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "Bird Hell Standalone";
    }

    protected override void Initialize()
    {
        _graphics.ApplyChanges();
        _run = new MainScreenRun(CaveGenerator.CreateRandomSeed(), _caveProfile);
        _flightRecorder = FlightRecorder.Start(_flightRecorderOptions, _run, SimulationStep);
        if (_flightRecorder is not null) _run.AttachFlightRecorder(_flightRecorder);
        _stallDiagnostics = FrameStallDiagnostics.Start(_stallDiagnosticOptions, _flightRecorder is not null);
        CompleteHostReset();
        _gamePadStates = new GamePadState[GamePad.MaximumGamePadCount];
        if (_stallDiagnostics is not null)
        {
            _gamePadPollingTimings = new StallSectionTiming[_gamePadStates.Length];
            _simulationStepTimings = new SimulationStepTimings[MaximumInstrumentedCatchUpSteps];
            _enemyNavigationStepTimings = new EnemyNavigationStepTimings[MaximumInstrumentedCatchUpSteps];
            for (var index = 0; index < _enemyNavigationStepTimings.Length; index++)
                _enemyNavigationStepTimings[index].Birds =
                    new EnemyNavigationBirdTimings[_run.Enemies.Enemies.Count];
            _sceneryLayerTimings = new StallSectionTiming[SceneryLayerCount];
            _sceneryBatchTimings = new RenderBatchTimings[SceneryLayerCount];
        }
        var keyboardState = Keyboard.GetState();
        SampleGamePads();
        _gameplayInput.UpdateFrame(keyboardState, Mouse.GetState(), _gamePadStates);
        _previousKeyboardState = keyboardState;
        base.Initialize();
    }

    protected override void LoadContent()
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, Content.RootDirectory);
        _spritePresentation = new SpritePresentation(
            GraphicsDevice,
            contentRoot);
        _environmentPresentation = new EnvironmentPresentation(GraphicsDevice, contentRoot);
        _audio = new GameplayAudio(Content);
        _audio.StartMusic();
        if (_run is not null)
            _environmentPresentation.SetRunLayout(_run.Seed, _run.Geometry.MinX, _run.Geometry.MaxX);
        _effect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
            World = Matrix.Identity,
            View = Matrix.Identity,
        };
    }

    protected override void Update(GameTime gameTime)
    {
        var updateTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        _stallDiagnostics?.ObserveCallbackStart("update", gameTime.TotalGameTime);
        var timings = default(UpdateTimings);

        var inputTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        var timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        var keyboardState = Keyboard.GetState();
        if (_stallDiagnostics is not null) timings.KeyboardPoll = FrameStallDiagnostics.Complete(timingStart);

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        var mouseState = Mouse.GetState();
        if (_stallDiagnostics is not null) timings.MousePoll = FrameStallDiagnostics.Complete(timingStart);

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        SampleGamePads(_stallDiagnostics is null ? null : _gamePadPollingTimings);
        if (_stallDiagnostics is not null) timings.GamePadPoll = FrameStallDiagnostics.Complete(timingStart);

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        _gameplayInput.UpdateFrame(keyboardState, mouseState, _gamePadStates);
        if (_stallDiagnostics is not null)
        {
            timings.InputAdapter = FrameStallDiagnostics.Complete(timingStart);
            timings.InputPoll = FrameStallDiagnostics.Complete(inputTimingStart);
        }

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        if (keyboardState.IsKeyDown(Keys.Escape)) Exit();
        if (_stallDiagnostics is not null) timings.ExitKeyHandling = FrameStallDiagnostics.Complete(timingStart);

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        var resetThisFrame = false;
        if (WasPressed(keyboardState, Keys.R))
        {
            var resetTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
            _run?.StartNewRun();
            if (_stallDiagnostics is not null)
                timings.RunRegeneration = FrameStallDiagnostics.Complete(resetTimingStart);
            resetTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
            CompleteHostReset();
            if (_stallDiagnostics is not null)
                timings.HostReset = FrameStallDiagnostics.Complete(resetTimingStart);
            resetThisFrame = true;
        }
        else if (WasPressed(keyboardState, Keys.F5))
        {
            var resetTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
            _run?.RestartSameSeed();
            if (_stallDiagnostics is not null)
                timings.RunRegeneration = FrameStallDiagnostics.Complete(resetTimingStart);
            resetTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
            CompleteHostReset();
            if (_stallDiagnostics is not null)
                timings.HostReset = FrameStallDiagnostics.Complete(resetTimingStart);
            resetThisFrame = true;
        }
        if (_stallDiagnostics is not null)
            timings.LifecycleKeyHandling = FrameStallDiagnostics.Complete(timingStart);

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        if (WasPressed(keyboardState, Keys.O))
        {
            _debug.ToggleOutlines();
            UpdateWindowTitle();
        }
        if (WasPressed(keyboardState, Keys.C))
        {
            _debug.ToggleCollision();
            UpdateWindowTitle();
        }
        if (WasPressed(keyboardState, Keys.N))
        {
            _debug.ToggleNavigation();
            UpdateWindowTitle();
        }
        if (WasPressed(keyboardState, Keys.V))
        {
            _atmosphere.ToggleVisibility();
            UpdateWindowTitle();
        }
        if (_stallDiagnostics is not null)
            timings.DebugKeyHandling = FrameStallDiagnostics.Complete(timingStart);

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        _atmosphere.Advance(gameTime.ElapsedGameTime.TotalSeconds);
        if (_stallDiagnostics is not null)
            timings.AtmosphereAdvance = FrameStallDiagnostics.Complete(timingStart);

        var fixedStepCount = 0;
        if (!resetThisFrame)
        {
            timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
            _simulationAccumulator += Math.Min(gameTime.ElapsedGameTime.TotalSeconds, MaximumFrameTime);
            if (_stallDiagnostics is not null)
                timings.AccumulatorPreparation = FrameStallDiagnostics.Complete(timingStart);

            var simulationTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
            while (_simulationAccumulator >= SimulationStep)
            {
                if (_stallDiagnostics is not null && fixedStepCount < _simulationStepTimings.Length)
                {
                    ref var stepTimings = ref _simulationStepTimings[fixedStepCount];
                    ref var navigationTimings = ref _enemyNavigationStepTimings[fixedStepCount];
                    stepTimings = default;
                    var retainedBirdTimings = navigationTimings.Birds;
                    navigationTimings = default;
                    navigationTimings.Birds = retainedBirdTimings;
                    var stepTimingStart = FrameStallDiagnostics.Capture();
                    Simulate(
                        _gameplayInput.ConsumeSimulationStep(),
                        ref stepTimings,
                        ref navigationTimings);
                    stepTimings.Total = FrameStallDiagnostics.Complete(stepTimingStart);
                }
                else
                {
                    Simulate(_gameplayInput.ConsumeSimulationStep());
                }
                _simulationAccumulator -= SimulationStep;
                fixedStepCount++;
            }
            if (_stallDiagnostics is not null)
                timings.SimulationCatchUp = FrameStallDiagnostics.Complete(simulationTimingStart);
        }

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        _previousKeyboardState = keyboardState;
        if (_stallDiagnostics is not null)
            timings.UpdateStateCommit = FrameStallDiagnostics.Complete(timingStart);

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        base.Update(gameTime);
        if (_stallDiagnostics is not null)
        {
            timings.BaseUpdate = FrameStallDiagnostics.Complete(timingStart);
            var updateTiming = FrameStallDiagnostics.Complete(updateTimingStart);
            var eventGroupId = _stallDiagnostics.RecordRootSection(
                "update", updateTiming, gameTime.TotalGameTime, fixedStepCount);
            RecordUpdateBreakdown(eventGroupId, timings, gameTime.TotalGameTime, fixedStepCount);
        }
        _stallDiagnostics?.ObserveCallbackEnd("update");
    }

    protected override void Draw(GameTime gameTime)
    {
        var drawTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        _stallDiagnostics?.ObserveCallbackStart("draw", gameTime.TotalGameTime);

        var timings = default(DrawTimings);
        var timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        GraphicsDevice.Clear(new Color(16, 23, 47));
        if (_stallDiagnostics is not null) timings.GraphicsClear = FrameStallDiagnostics.Complete(timingStart);

        var environmentTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        if (_environmentPresentation is not null)
        {
            if (_stallDiagnostics is null) _environmentPresentation.DrawSky();
            else _environmentPresentation.DrawSky(ref timings.SkyBatch);
        }
        if (_stallDiagnostics is not null) timings.Sky = FrameStallDiagnostics.Complete(timingStart);

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        if (_environmentPresentation is not null)
        {
            if (_stallDiagnostics is null) _environmentPresentation.DrawClouds(_camera, _atmosphere);
            else _environmentPresentation.DrawClouds(_camera, _atmosphere, ref timings.CloudBatch);
        }
        if (_stallDiagnostics is not null) timings.Clouds = FrameStallDiagnostics.Complete(timingStart);

        DrawSceneryLayerWithTiming(EnvironmentSceneryLayer.HugeMountains, 0);
        DrawSceneryLayerWithTiming(EnvironmentSceneryLayer.DistantCities, 1);
        DrawSceneryLayerWithTiming(EnvironmentSceneryLayer.Ruins, 2);
        DrawSceneryLayerWithTiming(EnvironmentSceneryLayer.NearRocks, 3);
        DrawSceneryLayerWithTiming(EnvironmentSceneryLayer.RarePlants, 4);
        if (_stallDiagnostics is not null)
            timings.Environment = FrameStallDiagnostics.Complete(environmentTimingStart);

        if (_run is not null && _effect is not null)
        {
            timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
            ConfigureCamera(_effect);
            if (_stallDiagnostics is not null)
                timings.CameraMatrixPreparation = FrameStallDiagnostics.Complete(timingStart);

            // Distant background and clouds were drawn first. Terrain, entities, player,
            // projectiles/future effects, and debug overlays follow in documented order.
            timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
            if (_stallDiagnostics is null) DrawCaveGeometry(_effect);
            else DrawCaveGeometry(_effect, ref timings.CaveBatch);
            if (_stallDiagnostics is not null) timings.CaveRender = FrameStallDiagnostics.Complete(timingStart);
            if (_spritePresentation is not null)
            {
                var spriteTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
                var batchTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();

                timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
                _spritePresentation.BeginWorldSprites(_camera, BirdHellVisuals.Enemy.Frames[0].Region.Filter);
                if (_stallDiagnostics is not null)
                    timings.EnemyBatchBegin = FrameStallDiagnostics.Complete(timingStart);

                timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
                DrawEnemies(_spritePresentation);
                if (_stallDiagnostics is not null)
                    timings.EnemyRender = FrameStallDiagnostics.Complete(timingStart);

                timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
                _spritePresentation.EndWorldSprites();
                if (_stallDiagnostics is not null)
                {
                    timings.EnemyBatchEnd = FrameStallDiagnostics.Complete(timingStart);
                    timings.EnemyBatch = FrameStallDiagnostics.Complete(batchTimingStart);
                }

                batchTimingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
                timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
                _spritePresentation.BeginWorldSprites(_camera, BirdHellVisuals.PlayerIdle.Frames[0].Region.Filter);
                if (_stallDiagnostics is not null)
                    timings.PlayerProjectileBatchBegin = FrameStallDiagnostics.Complete(timingStart);

                timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
                DrawPlayer(_spritePresentation);
                if (_stallDiagnostics is not null)
                    timings.PlayerRender = FrameStallDiagnostics.Complete(timingStart);

                timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
                DrawProjectiles(_spritePresentation);
                if (_stallDiagnostics is not null)
                    timings.ProjectileRender = FrameStallDiagnostics.Complete(timingStart);

                timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
                _spritePresentation.EndWorldSprites();
                if (_stallDiagnostics is not null)
                {
                    timings.PlayerProjectileBatchEnd = FrameStallDiagnostics.Complete(timingStart);
                    timings.PlayerProjectileBatch = FrameStallDiagnostics.Complete(batchTimingStart);
                    timings.GameplaySprites = FrameStallDiagnostics.Complete(spriteTimingStart);
                }
            }

            timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
            if (_stallDiagnostics is null) DrawDebugOverlays(_effect);
            else DrawDebugOverlays(_effect, ref timings.DebugOverlayChildren);
            if (_stallDiagnostics is not null)
                timings.DebugOverlays = FrameStallDiagnostics.Complete(timingStart);
        }

        timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        base.Draw(gameTime);
        if (_stallDiagnostics is not null) timings.BaseDraw = FrameStallDiagnostics.Complete(timingStart);

        if (_stallDiagnostics is not null)
        {
            var eventGroupId = _stallDiagnostics.RecordRootSection(
                "draw", FrameStallDiagnostics.Complete(drawTimingStart), gameTime.TotalGameTime);
            RecordDrawBreakdown(eventGroupId, timings, gameTime.TotalGameTime);
        }
        _stallDiagnostics?.ObserveCallbackEnd("draw");
    }

    protected override void UnloadContent()
    {
        _flightRecorder?.Dispose();
        _stallDiagnostics?.Dispose();
        _environmentPresentation?.Dispose();
        _spritePresentation?.Dispose();
        _audio?.Dispose();
        _effect?.Dispose();
    }

    private void DrawSceneryLayerWithTiming(EnvironmentSceneryLayer layer, int timingIndex)
    {
        var timingStart = _stallDiagnostics is null ? default : FrameStallDiagnostics.Capture();
        if (_environmentPresentation is not null)
        {
            if (_stallDiagnostics is null)
                _environmentPresentation.DrawSceneryLayer(_camera, layer);
            else
            {
                _sceneryBatchTimings[timingIndex] = default;
                _environmentPresentation.DrawSceneryLayer(
                    _camera, layer, ref _sceneryBatchTimings[timingIndex]);
            }
        }
        if (_stallDiagnostics is not null)
            _sceneryLayerTimings[timingIndex] = FrameStallDiagnostics.Complete(timingStart);
    }

    private void RecordDrawBreakdown(long eventGroupId, DrawTimings timings, TimeSpan gameTime)
    {
        if (_stallDiagnostics is null || eventGroupId == 0) return;

        _stallDiagnostics.RecordNestedSection(
            "graphics_device_clear", "draw", eventGroupId, timings.GraphicsClear, gameTime);
        if (_stallDiagnostics.RecordNestedSection(
                "environment_render", "draw", eventGroupId, timings.Environment, gameTime))
        {
            if (_stallDiagnostics.RecordNestedSection(
                    "sky_render", "environment_render", eventGroupId, timings.Sky, gameTime))
                RecordRenderBatchBreakdown(eventGroupId, "sky_render", timings.SkyBatch, gameTime);
            if (_stallDiagnostics.RecordNestedSection(
                    "cloud_render", "environment_render", eventGroupId, timings.Clouds, gameTime))
                RecordRenderBatchBreakdown(eventGroupId, "cloud_render", timings.CloudBatch, gameTime);

            for (var index = 0; index < SceneryLayerCount; index++)
            {
                var section = ScenerySection(index);
                if (_stallDiagnostics.RecordNestedSection(
                        section, "environment_render", eventGroupId, _sceneryLayerTimings[index], gameTime))
                    RecordRenderBatchBreakdown(eventGroupId, section, _sceneryBatchTimings[index], gameTime);
            }
        }

        _stallDiagnostics.RecordNestedSection(
            "camera_matrix_preparation", "draw", eventGroupId, timings.CameraMatrixPreparation, gameTime);
        if (_stallDiagnostics.RecordNestedSection(
                "cave_primitive_render", "draw", eventGroupId, timings.CaveRender, gameTime))
        {
            _stallDiagnostics.RecordNestedSection(
                "graphics_state_setup",
                "cave_primitive_render",
                eventGroupId,
                timings.CaveBatch.Preparation,
                gameTime);
            _stallDiagnostics.RecordNestedSection(
                "effect_pass_and_primitive_draw",
                "cave_primitive_render",
                eventGroupId,
                timings.CaveBatch.Submission,
                gameTime);
        }

        if (_stallDiagnostics.RecordNestedSection(
                "gameplay_sprite_render", "draw", eventGroupId, timings.GameplaySprites, gameTime))
        {
            if (_stallDiagnostics.RecordNestedSection(
                    "enemy_sprite_batch", "gameplay_sprite_render", eventGroupId, timings.EnemyBatch, gameTime))
            {
                _stallDiagnostics.RecordNestedSection(
                    "sprite_batch_begin", "enemy_sprite_batch", eventGroupId, timings.EnemyBatchBegin, gameTime);
                _stallDiagnostics.RecordNestedSection(
                    "enemy_submission", "enemy_sprite_batch", eventGroupId, timings.EnemyRender, gameTime);
                _stallDiagnostics.RecordNestedSection(
                    "sprite_batch_end", "enemy_sprite_batch", eventGroupId, timings.EnemyBatchEnd, gameTime);
            }

            if (_stallDiagnostics.RecordNestedSection(
                    "player_projectile_sprite_batch",
                    "gameplay_sprite_render",
                    eventGroupId,
                    timings.PlayerProjectileBatch,
                    gameTime))
            {
                _stallDiagnostics.RecordNestedSection(
                    "sprite_batch_begin",
                    "player_projectile_sprite_batch",
                    eventGroupId,
                    timings.PlayerProjectileBatchBegin,
                    gameTime);
                _stallDiagnostics.RecordNestedSection(
                    "player_submission",
                    "player_projectile_sprite_batch",
                    eventGroupId,
                    timings.PlayerRender,
                    gameTime);
                _stallDiagnostics.RecordNestedSection(
                    "projectile_submission",
                    "player_projectile_sprite_batch",
                    eventGroupId,
                    timings.ProjectileRender,
                    gameTime);
                _stallDiagnostics.RecordNestedSection(
                    "sprite_batch_end",
                    "player_projectile_sprite_batch",
                    eventGroupId,
                    timings.PlayerProjectileBatchEnd,
                    gameTime);
            }
        }

        if (_stallDiagnostics.RecordNestedSection(
                "debug_overlays", "draw", eventGroupId, timings.DebugOverlays, gameTime))
        {
            _stallDiagnostics.RecordNestedSection(
                "terrain_outline_overlay",
                "debug_overlays",
                eventGroupId,
                timings.DebugOverlayChildren.TerrainOutlines,
                gameTime);
            _stallDiagnostics.RecordNestedSection(
                "collision_and_aim_overlay",
                "debug_overlays",
                eventGroupId,
                timings.DebugOverlayChildren.CollisionAndAim,
                gameTime);
            _stallDiagnostics.RecordNestedSection(
                "navigation_overlay",
                "debug_overlays",
                eventGroupId,
                timings.DebugOverlayChildren.Navigation,
                gameTime);
        }
        _stallDiagnostics.RecordNestedSection(
            "base_draw", "draw", eventGroupId, timings.BaseDraw, gameTime);
    }

    private void RecordRenderBatchBreakdown(
        long eventGroupId,
        string parentSection,
        RenderBatchTimings timings,
        TimeSpan gameTime)
    {
        if (_stallDiagnostics is null) return;
        _stallDiagnostics.RecordNestedSection(
            "cpu_preparation", parentSection, eventGroupId, timings.Preparation, gameTime);
        _stallDiagnostics.RecordNestedSection(
            "sprite_batch_begin", parentSection, eventGroupId, timings.Begin, gameTime);
        _stallDiagnostics.RecordNestedSection(
            "sprite_submission", parentSection, eventGroupId, timings.Submission, gameTime);
        _stallDiagnostics.RecordNestedSection(
            "sprite_batch_end", parentSection, eventGroupId, timings.End, gameTime);
    }

    private static string ScenerySection(int index) => index switch
    {
        0 => "scenery_huge_mountains",
        1 => "scenery_distant_cities",
        2 => "scenery_ruins",
        3 => "scenery_near_rocks",
        4 => "scenery_rare_plants",
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private bool WasPressed(KeyboardState currentState, Keys key) =>
        currentState.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);

    private void CompleteHostReset()
    {
        if (_run is null) return;
        _simulationAccumulator = 0d;
        _gameplayInput.ClearBufferedEdges();
        _atmosphere.OnGameplayReset();
        _environmentPresentation?.SetRunLayout(_run.Seed, _run.Geometry.MinX, _run.Geometry.MaxX);
        var keyboard = Keyboard.GetState();
        _animations.Reset(CreatePlayerVisualSnapshot(
            keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down)));
        BuildRenderData(_run.Geometry);
        UpdateWindowTitle();
    }

    private void BuildRenderData(CaveGeometry geometry)
    {
        var terrainColor = new Color(218, 209, 176);
        _terrainVertices = geometry.Vertices
            .Select(vertex => new VertexPositionColor(new Vector3(vertex.X, vertex.Y, 0f), terrainColor))
            .ToArray();
        _triangleIndices = geometry.Triangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .ToArray();

        var outlineColor = new Color(255, 54, 149);
        var lineVertices = new List<VertexPositionColor>();
        foreach (var outline in geometry.Outlines)
            for (var i = 0; i < outline.Points.Count - 1; i++)
            {
                var first = outline.Points[i];
                var second = outline.Points[i + 1];
                lineVertices.Add(new VertexPositionColor(new Vector3(first.X, first.Y, -0.1f), outlineColor));
                lineVertices.Add(new VertexPositionColor(new Vector3(second.X, second.Y, -0.1f), outlineColor));
            }
        _outlineVertices = lineVertices.ToArray();
    }

    private void ConfigureCamera(BasicEffect effect)
    {
        var viewport = GraphicsDevice.Viewport;
        var view = _camera.GetView(viewport.Width, viewport.Height);

        effect.Projection = Matrix.CreateOrthographicOffCenter(
            view.Left,
            view.Right,
            view.Bottom,
            view.Top,
            0f,
            1f);
    }

    private void DrawCaveGeometry(BasicEffect effect)
    {
        var unusedTimings = default(RenderBatchTimings);
        DrawCaveGeometryCore(effect, ref unusedTimings, false);
    }

    private void DrawCaveGeometry(BasicEffect effect, ref RenderBatchTimings timings) =>
        DrawCaveGeometryCore(effect, ref timings, true);

    private void DrawCaveGeometryCore(BasicEffect effect, ref RenderBatchTimings timings, bool captureTimings)
    {
        var timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        if (captureTimings) timings.Preparation = FrameStallDiagnostics.Complete(timingStart);

        timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (_triangleIndices.Length > 0)
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _terrainVertices,
                    0,
                    _terrainVertices.Length,
                    _triangleIndices,
                    0,
                    _triangleIndices.Length / 3);
        }
        if (captureTimings) timings.Submission = FrameStallDiagnostics.Complete(timingStart);
    }

    private void DrawPlayer(SpritePresentation sprites)
    {
        if (_run is null || !_animations.Player.ShouldRender || _run.State == RunState.PlayerDead) return;
        var player = _run.Player;
        sprites.Draw(
            _animations.Player.Playback.CurrentFrame,
            player.Position,
            PlayerAnimationState.FlipForFacing(player.FacingDirection),
            HitFeedbackPresentation.TintFor(player.DamageFlashTime));
    }

    private static IReadOnlyList<Microsoft.Xna.Framework.Vector2> BuildPlayerPolygon(Player player)
    {
        const int arcSteps = 8;
        var collider = player.Collider;
        var center = player.ColliderCenter;
        var polygon = new List<Microsoft.Xna.Framework.Vector2>();
        for (var i = 0; i <= arcSteps; i++)
        {
            var angle = i * MathF.PI / arcSteps;
            polygon.Add(new Microsoft.Xna.Framework.Vector2(
                center.X + MathF.Cos(angle) * collider.Radius,
                center.Y + collider.CoreHalfHeight + MathF.Sin(angle) * collider.Radius));
        }
        for (var i = 0; i <= arcSteps; i++)
        {
            var angle = MathF.PI + i * MathF.PI / arcSteps;
            polygon.Add(new Microsoft.Xna.Framework.Vector2(
                center.X + MathF.Cos(angle) * collider.Radius,
                center.Y - collider.CoreHalfHeight + MathF.Sin(angle) * collider.Radius));
        }
        return polygon;
    }

    private void Simulate(GameplayInputState input)
    {
        var unusedTimings = default(SimulationStepTimings);
        var unusedNavigationTimings = default(EnemyNavigationStepTimings);
        SimulateCore(input, ref unusedTimings, ref unusedNavigationTimings, false);
    }

    private void Simulate(
        GameplayInputState input,
        ref SimulationStepTimings timings,
        ref EnemyNavigationStepTimings navigationTimings) =>
        SimulateCore(input, ref timings, ref navigationTimings, true);

    private void SimulateCore(
        GameplayInputState input,
        ref SimulationStepTimings timings,
        ref EnemyNavigationStepTimings navigationTimings,
        bool captureTimings)
    {
        if (_run is null) return;
        var shotsBefore = _run.WeaponState.ShotsFired;
        var timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        MainScreenStepResult stepResult;
        if (captureTimings)
            stepResult = _run.Step(
                new PlayerInput(input.Horizontal, input.JumpPressed, input.JumpHeld),
                input.FirePressed,
                SimulationStep,
                ref timings.RunSystems,
                ref navigationTimings);
        else
            stepResult = _run.Step(
                new PlayerInput(input.Horizontal, input.JumpPressed, input.JumpHeld),
                input.FirePressed,
                SimulationStep);
        if (captureTimings) timings.RunStep = FrameStallDiagnostics.Complete(timingStart);

        timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        if (_audio is not null) GameplayFeedbackAudioRouter.Route(stepResult.Feedback, _audio);
        if (captureTimings) timings.AudioFeedback = FrameStallDiagnostics.Complete(timingStart);

        timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        _animations.Player.Update(
            CreatePlayerVisualSnapshot(input.CrouchHeld),
            _run.WeaponState.ShotsFired > shotsBefore,
            stepResult.SecondJumpPerformed,
            SimulationStep);
        _animations.AdvanceEnemies(SimulationStep);
        if (captureTimings) timings.AnimationAdvance = FrameStallDiagnostics.Complete(timingStart);

        timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        _camera.Step(_run.Player.Position);
        if (captureTimings) timings.CameraStep = FrameStallDiagnostics.Complete(timingStart);

        timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        _navigationDiagnosticTitleTimer += SimulationStep;
        if (_displayedHealth != _run.Player.Health || _displayedEnemyCount != _run.Enemies.LivingCount ||
            _displayedAllEnemiesDead != _run.Enemies.AllEnemiesDead)
            UpdateWindowTitle();
        else if (_debug.ShowNavigation && _navigationDiagnosticTitleTimer >= 0.25f)
            UpdateWindowTitle();
        if (captureTimings) timings.StatusAndWindowTitle = FrameStallDiagnostics.Complete(timingStart);
    }

    private void SampleGamePads(StallSectionTiming[]? perControllerTimings = null)
    {
        for (var index = 0; index < _gamePadStates.Length; index++)
        {
            var timingStart = perControllerTimings is null ? default : FrameStallDiagnostics.Capture();
            _gamePadStates[index] = GamePad.GetState(index, GamePadDeadZone.None);
            if (perControllerTimings is not null)
                perControllerTimings[index] = FrameStallDiagnostics.Complete(timingStart);
        }
    }

    private void RecordUpdateBreakdown(
        long eventGroupId,
        UpdateTimings timings,
        TimeSpan gameTime,
        int fixedStepCount)
    {
        if (_stallDiagnostics is null || eventGroupId == 0) return;

        if (_stallDiagnostics.RecordNestedSection(
                "input_poll", "update", eventGroupId, timings.InputPoll, gameTime))
        {
            _stallDiagnostics.RecordNestedSection(
                "keyboard_poll", "input_poll", eventGroupId, timings.KeyboardPoll, gameTime);
            _stallDiagnostics.RecordNestedSection(
                "mouse_poll", "input_poll", eventGroupId, timings.MousePoll, gameTime);
            if (_stallDiagnostics.RecordNestedSection(
                    "gamepad_poll", "input_poll", eventGroupId, timings.GamePadPoll, gameTime))
                for (var index = 0; index < _gamePadPollingTimings.Length; index++)
                    _stallDiagnostics.RecordNestedSection(
                        $"gamepad_poll_index_{index}",
                        "gamepad_poll",
                        eventGroupId,
                        _gamePadPollingTimings[index],
                        gameTime);
            _stallDiagnostics.RecordNestedSection(
                "gameplay_input_adapter", "input_poll", eventGroupId, timings.InputAdapter, gameTime);
        }

        _stallDiagnostics.RecordNestedSection(
            "exit_key_handling", "update", eventGroupId, timings.ExitKeyHandling, gameTime);
        if (_stallDiagnostics.RecordNestedSection(
                "lifecycle_key_handling", "update", eventGroupId, timings.LifecycleKeyHandling, gameTime))
        {
            _stallDiagnostics.RecordNestedSection(
                "run_regeneration", "lifecycle_key_handling", eventGroupId, timings.RunRegeneration, gameTime);
            _stallDiagnostics.RecordNestedSection(
                "host_reset", "lifecycle_key_handling", eventGroupId, timings.HostReset, gameTime);
        }
        _stallDiagnostics.RecordNestedSection(
            "debug_key_handling", "update", eventGroupId, timings.DebugKeyHandling, gameTime);
        _stallDiagnostics.RecordNestedSection(
            "atmosphere_advance", "update", eventGroupId, timings.AtmosphereAdvance, gameTime);
        _stallDiagnostics.RecordNestedSection(
            "accumulator_preparation", "update", eventGroupId, timings.AccumulatorPreparation, gameTime);

        if (_stallDiagnostics.RecordNestedSection(
                "simulation_catch_up",
                "update",
                eventGroupId,
                timings.SimulationCatchUp,
                gameTime,
                fixedStepCount))
        {
            var recordedSteps = Math.Min(fixedStepCount, _simulationStepTimings.Length);
            for (var index = 0; index < recordedSteps; index++)
                RecordSimulationStepBreakdown(eventGroupId, index, _simulationStepTimings[index], gameTime);
        }

        _stallDiagnostics.RecordNestedSection(
            "update_state_commit", "update", eventGroupId, timings.UpdateStateCommit, gameTime);
        _stallDiagnostics.RecordNestedSection(
            "base_update", "update", eventGroupId, timings.BaseUpdate, gameTime);
    }

    private void RecordSimulationStepBreakdown(
        long eventGroupId,
        int stepIndex,
        SimulationStepTimings timings,
        TimeSpan gameTime)
    {
        if (_stallDiagnostics is null) return;
        var stepSection = $"simulation_step_{stepIndex}";
        if (!_stallDiagnostics.RecordNestedSection(
                stepSection, "simulation_catch_up", eventGroupId, timings.Total, gameTime, 1))
            return;

        if (_stallDiagnostics.RecordNestedSection(
                "run_step", stepSection, eventGroupId, timings.RunStep, gameTime, 1))
        {
            var systems = timings.RunSystems;
            _stallDiagnostics.RecordNestedSection(
                "flight_recorder_before_step", "run_step", eventGroupId, systems.FlightRecorderBefore, gameTime);
            _stallDiagnostics.RecordNestedSection(
                "feedback_advance", "run_step", eventGroupId, systems.FeedbackAdvance, gameTime);
            _stallDiagnostics.RecordNestedSection(
                "player_movement", "run_step", eventGroupId, systems.PlayerMovement, gameTime);
            _stallDiagnostics.RecordNestedSection(
                "weapon", "run_step", eventGroupId, systems.Weapon, gameTime);
            _stallDiagnostics.RecordNestedSection(
                "projectiles", "run_step", eventGroupId, systems.Projectiles, gameTime);
            if (_stallDiagnostics.RecordNestedSection(
                    "enemy_navigation", "run_step", eventGroupId, systems.EnemyNavigation, gameTime))
                RecordEnemyNavigationBreakdown(
                    eventGroupId, _enemyNavigationStepTimings[stepIndex], gameTime);
            _stallDiagnostics.RecordNestedSection(
                "enemy_combat", "run_step", eventGroupId, systems.EnemyCombat, gameTime);
            _stallDiagnostics.RecordNestedSection(
                "flight_recorder_after_step", "run_step", eventGroupId, systems.FlightRecorderAfter, gameTime);
        }
        _stallDiagnostics.RecordNestedSection(
            "audio_feedback", stepSection, eventGroupId, timings.AudioFeedback, gameTime);
        _stallDiagnostics.RecordNestedSection(
            "animation_advance", stepSection, eventGroupId, timings.AnimationAdvance, gameTime);
        _stallDiagnostics.RecordNestedSection(
            "camera_step", stepSection, eventGroupId, timings.CameraStep, gameTime);
        _stallDiagnostics.RecordNestedSection(
            "status_and_window_title", stepSection, eventGroupId, timings.StatusAndWindowTitle, gameTime);
    }

    private void RecordEnemyNavigationBreakdown(
        long eventGroupId,
        EnemyNavigationStepTimings timings,
        TimeSpan gameTime)
    {
        if (_stallDiagnostics is null) return;
        const string parentSection = "enemy_navigation";
        _stallDiagnostics.RecordFineNestedSection(
            "navigation_preparation", parentSection, eventGroupId, timings.Preparation, gameTime);

        var birds = timings.Birds;
        if (birds is not null)
            for (var index = 0; index < timings.BirdCount; index++)
            {
                var bird = birds[index];
                var birdSection = $"enemy_navigation_bird_{bird.EnemyId}";
                if (!_stallDiagnostics.RecordFineNestedSection(
                        birdSection, parentSection, eventGroupId, bird.Total, gameTime))
                    continue;

                if (bird.Repathed)
                {
                    var repathSection = $"bird_{bird.EnemyId}_repath";
                    if (_stallDiagnostics.RecordFineNestedSection(
                            repathSection, birdSection, eventGroupId, bird.Repath, gameTime))
                    {
                        var repath = bird.RepathDetails;
                        _stallDiagnostics.RecordFineNestedSection(
                            "endpoint_resolution", repathSection, eventGroupId, repath.EndpointResolution, gameTime);
                        _stallDiagnostics.RecordFineNestedSection(
                            "start_radius_clear_cell_search",
                            repathSection,
                            eventGroupId,
                            repath.StartRadiusClearSearch,
                            gameTime);
                        _stallDiagnostics.RecordFineNestedSection(
                            "destination_radius_clear_cell_search",
                            repathSection,
                            eventGroupId,
                            repath.DestinationRadiusClearSearch,
                            gameTime);
                        _stallDiagnostics.RecordFineNestedSection(
                            "resolved_endpoint_validation",
                            repathSection,
                            eventGroupId,
                            repath.ResolvedEndpointValidation,
                            gameTime);
                        _stallDiagnostics.RecordFineNestedSection(
                            "a_star", repathSection, eventGroupId, repath.AStar, gameTime);
                        _stallDiagnostics.RecordFineNestedSection(
                            "direct_target_check", repathSection, eventGroupId, repath.DirectTargetCheck, gameTime);
                    }
                }

                _stallDiagnostics.RecordFineNestedSection(
                    "movement_and_steering",
                    birdSection,
                    eventGroupId,
                    bird.MovementAndSteering,
                    gameTime);
            }

        _stallDiagnostics.RecordFineNestedSection(
            "enemy_separation", parentSection, eventGroupId, timings.Separation, gameTime);
        _stallDiagnostics.RecordFineNestedSection(
            "navigation_state_update", parentSection, eventGroupId, timings.StateUpdate, gameTime);
    }

    private PlayerVisualSnapshot CreatePlayerVisualSnapshot(bool crouchHeld)
    {
        if (_run is null) return default;
        return new PlayerVisualSnapshot(
            _run.Player.IsAlive,
            _run.Player.IsGrounded,
            crouchHeld,
            _run.Player.Velocity.X);
    }

    private void DrawEnemies(SpritePresentation sprites)
    {
        if (_run is null) return;
        foreach (var enemy in _run.Enemies.LivingEnemies)
            sprites.Draw(
                _animations.Enemy.CurrentFrame,
                enemy.Position,
                WorldAnimationState.EnemyFlip(enemy.FacingDirection),
                HitFeedbackPresentation.TintFor(enemy.DamageFlashTime));
    }

    private void DrawNavigationDebug(BasicEffect effect)
    {
        if (_run is null) return;
        var navigationGrid = _run.NavigationGrid;
        var diagnostics = _run.EnemyNavigation.GetDiagnostics(_run.Enemies.Enemies, _run.Player.Position);
        var lines = new List<VertexPositionColor>();
        var blockedColor = new Color(170, 40, 210, 110);
        for (var y = 0; y < navigationGrid.Height; y += 2)
        for (var x = 0; x < navigationGrid.Width; x += 2)
        {
            var cell = new NavigationCell(x, y);
            if (navigationGrid.IsWalkable(cell)) continue;
            var center = navigationGrid.CellToWorld(cell);
            lines.Add(new VertexPositionColor(new Vector3(center.X - 0.18f, center.Y, -0.45f), blockedColor));
            lines.Add(new VertexPositionColor(new Vector3(center.X + 0.18f, center.Y, -0.45f), blockedColor));
        }

        foreach (var item in diagnostics.Where(item =>
                     item.StallDuration >= 0.5f || item.RecentlyCollidedWithTerrain))
        {
            var enemy = _run.Enemies.LivingEnemies.First(candidate => candidate.Id == item.EnemyId);
            var center = enemy.ColliderCenter;
            var color = item.StallDuration >= 0.5f ? Color.Red : Color.Orange;
            lines.Add(new VertexPositionColor(new Vector3(center.X - 0.3f, center.Y - 0.3f, -0.48f), color));
            lines.Add(new VertexPositionColor(new Vector3(center.X + 0.3f, center.Y + 0.3f, -0.48f), color));
            lines.Add(new VertexPositionColor(new Vector3(center.X - 0.3f, center.Y + 0.3f, -0.48f), color));
            lines.Add(new VertexPositionColor(new Vector3(center.X + 0.3f, center.Y - 0.3f, -0.48f), color));
        }

        var selectedDiagnostic = diagnostics
            .OrderByDescending(item => item.StallDuration)
            .ThenByDescending(item => item.RecentlyCollidedWithTerrain)
            .ThenByDescending(item => item.NearbyEnemyCount)
            .ThenBy(item => item.EnemyId)
            .FirstOrDefault();
        var selected = selectedDiagnostic is null
            ? null
            : _run.Enemies.LivingEnemies.First(enemy => enemy.Id == selectedDiagnostic.EnemyId);
        if (selected is not null)
        {
            for (var index = Math.Max(0, selected.PathIndex - 1); index < selected.Path.Count - 1; index++)
            {
                var first = selected.Path[index];
                var second = selected.Path[index + 1];
                lines.Add(new VertexPositionColor(new Vector3(first.X, first.Y, -0.46f), Color.LimeGreen));
                lines.Add(new VertexPositionColor(new Vector3(second.X, second.Y, -0.46f), Color.LimeGreen));
            }
            lines.Add(new VertexPositionColor(new Vector3(selected.ColliderCenter.X, selected.ColliderCenter.Y, -0.47f), Color.Yellow));
            lines.Add(new VertexPositionColor(new Vector3(selected.SteeringTarget.X, selected.SteeringTarget.Y, -0.47f), Color.Yellow));
        }

        if (lines.Count == 0) return;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
        }
    }

    private void DrawProjectiles(SpritePresentation sprites)
    {
        if (_run is null) return;
        foreach (var projectile in _run.Projectiles.Projectiles)
        {
            var frameIndex = WorldAnimationState.ProjectileFrame(projectile.Age);
            sprites.Draw(
                BirdHellVisuals.Projectile.Frames[frameIndex],
                projectile.Position,
                WorldAnimationState.ProjectileFlip(projectile.Direction.X),
                Color.White);
        }
    }

    private void DrawDebugOverlays(BasicEffect effect)
    {
        var unusedTimings = default(DebugOverlayTimings);
        DrawDebugOverlaysCore(effect, ref unusedTimings, false);
    }

    private void DrawDebugOverlays(BasicEffect effect, ref DebugOverlayTimings timings) =>
        DrawDebugOverlaysCore(effect, ref timings, true);

    private void DrawDebugOverlaysCore(
        BasicEffect effect,
        ref DebugOverlayTimings timings,
        bool captureTimings)
    {
        var timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        if (_debug.ShowOutlines) DrawTerrainOutlineDebug(effect);
        if (captureTimings) timings.TerrainOutlines = FrameStallDiagnostics.Complete(timingStart);

        timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        if (_debug.ShowCollision)
        {
            DrawPlayerCollisionDebug(effect);
            DrawAimDebug(effect);
        }
        if (captureTimings) timings.CollisionAndAim = FrameStallDiagnostics.Complete(timingStart);

        timingStart = captureTimings ? FrameStallDiagnostics.Capture() : default;
        if (_debug.ShowNavigation) DrawNavigationDebug(effect);
        if (captureTimings) timings.Navigation = FrameStallDiagnostics.Complete(timingStart);
    }

    private void DrawTerrainOutlineDebug(BasicEffect effect)
    {
        if (_outlineVertices.Length == 0) return;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.LineList,
                _outlineVertices,
                0,
                _outlineVertices.Length / 2);
        }
    }

    private void DrawPlayerCollisionDebug(BasicEffect effect)
    {
        if (_run is null) return;
        var player = _run.Player;
        var center = player.ColliderCenter;
        var polygon = BuildPlayerPolygon(player);
        var debugColor = player.IsGrounded ? Color.LimeGreen : Color.Orange;
        var lines = new List<VertexPositionColor>();
        for (var index = 0; index < polygon.Count; index++)
        {
            lines.Add(new VertexPositionColor(new Vector3(polygon[index], -0.3f), debugColor));
            lines.Add(new VertexPositionColor(
                new Vector3(polygon[(index + 1) % polygon.Count], -0.3f),
                debugColor));
        }
        lines.Add(new VertexPositionColor(new Vector3(center.X, center.Y, -0.3f), Color.Yellow));
        lines.Add(new VertexPositionColor(
            new Vector3(center.X + player.GroundNormal.X, center.Y + player.GroundNormal.Y, -0.3f),
            Color.Yellow));
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
        }
    }

    private void DrawAimDebug(BasicEffect effect)
    {
        if (_run is null) return;
        var direction = _run.GetAimDirection();
        var muzzle = _run.GetMuzzlePosition();
        var aimEnd = muzzle + direction * 1.5f;
        VertexPositionColor[] vertices =
        [
            new VertexPositionColor(new Vector3(muzzle.X, muzzle.Y, -0.35f), new Color(255, 196, 64)),
            new VertexPositionColor(new Vector3(aimEnd.X, aimEnd.Y, -0.35f), new Color(255, 196, 64)),
        ];
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, vertices, 0, 1);
        }
    }

    private struct UpdateTimings
    {
        public StallSectionTiming InputPoll;
        public StallSectionTiming KeyboardPoll;
        public StallSectionTiming MousePoll;
        public StallSectionTiming GamePadPoll;
        public StallSectionTiming InputAdapter;
        public StallSectionTiming ExitKeyHandling;
        public StallSectionTiming LifecycleKeyHandling;
        public StallSectionTiming RunRegeneration;
        public StallSectionTiming HostReset;
        public StallSectionTiming DebugKeyHandling;
        public StallSectionTiming AtmosphereAdvance;
        public StallSectionTiming AccumulatorPreparation;
        public StallSectionTiming SimulationCatchUp;
        public StallSectionTiming UpdateStateCommit;
        public StallSectionTiming BaseUpdate;
    }

    private struct SimulationStepTimings
    {
        public StallSectionTiming Total;
        public StallSectionTiming RunStep;
        public MainScreenStepTimings RunSystems;
        public StallSectionTiming AudioFeedback;
        public StallSectionTiming AnimationAdvance;
        public StallSectionTiming CameraStep;
        public StallSectionTiming StatusAndWindowTitle;
    }

    private struct DrawTimings
    {
        public StallSectionTiming GraphicsClear;
        public StallSectionTiming Environment;
        public StallSectionTiming Sky;
        public RenderBatchTimings SkyBatch;
        public StallSectionTiming Clouds;
        public RenderBatchTimings CloudBatch;
        public StallSectionTiming CameraMatrixPreparation;
        public StallSectionTiming CaveRender;
        public RenderBatchTimings CaveBatch;
        public StallSectionTiming GameplaySprites;
        public StallSectionTiming EnemyBatch;
        public StallSectionTiming EnemyBatchBegin;
        public StallSectionTiming EnemyRender;
        public StallSectionTiming EnemyBatchEnd;
        public StallSectionTiming PlayerProjectileBatch;
        public StallSectionTiming PlayerProjectileBatchBegin;
        public StallSectionTiming PlayerRender;
        public StallSectionTiming ProjectileRender;
        public StallSectionTiming PlayerProjectileBatchEnd;
        public StallSectionTiming DebugOverlays;
        public DebugOverlayTimings DebugOverlayChildren;
        public StallSectionTiming BaseDraw;
    }

    private struct DebugOverlayTimings
    {
        public StallSectionTiming TerrainOutlines;
        public StallSectionTiming CollisionAndAim;
        public StallSectionTiming Navigation;
    }

    private void UpdateWindowTitle()
    {
        if (_run is null) return;
        _navigationDiagnosticTitleTimer = 0f;
        _displayedHealth = _run.Player.Health;
        _displayedEnemyCount = _run.Enemies.LivingCount;
        _displayedAllEnemiesDead = _run.Enemies.AllEnemiesDead;
        var state = _run.State switch
        {
            RunState.PlayerDead => " - PLAYER DEAD",
            RunState.AllBirdsDead => " - ALL BIRDS DEAD",
            _ => " - PLAYING",
        };
        var navigationDiagnostic = string.Empty;
        if (_debug.ShowNavigation)
        {
            var item = _run.EnemyNavigation.GetDiagnostics(_run.Enemies.Enemies, _run.Player.Position)
                .OrderByDescending(item => item.StallDuration)
                .ThenByDescending(item => item.RecentlyCollidedWithTerrain)
                .ThenByDescending(item => item.NearbyEnemyCount)
                .ThenBy(item => item.EnemyId)
                .FirstOrDefault();
            if (item is not null)
            {
                var waypoint = item.CurrentWaypoint is { } target ? $"{target.X:F1},{target.Y:F1}" : "none";
                navigationDiagnostic =
                    $" - N bird {item.EnemyId} pos {item.Position.X:F1},{item.Position.Y:F1} " +
                    $"vel {item.Velocity.X:F1},{item.Velocity.Y:F1} wp {waypoint} " +
                    $"move {item.LastStepDistance:F3} progress {item.PlayerDistanceDelta:+0.000;-0.000;0.000} " +
                    $"dist {item.PlayerDistance:F1} comp {item.NavigationComponentId} " +
                    $"collision {(item.RecentlyCollidedWithTerrain ? "yes" : "no")} " +
                    $"near {item.NearbyEnemyCount} stall {item.StallDuration:F1}s";
            }
        }
        Window.Title = $"Bird Hell [{_run.CaveProfileName}] - HP {_displayedHealth}/100 - Enemies {_displayedEnemyCount}/15{state}{navigationDiagnostic} - " +
                       $"A/D or left stick move, SPACE/A jump, S/Down crouch, LMB/Ctrl/RT fire, R new, F5 same, O outlines, C collision, N navigation, V clouds ({(_atmosphere.IsVisible ? "on" : "off")}), ESC exit - Seed: {_run.Seed}";
    }
}
