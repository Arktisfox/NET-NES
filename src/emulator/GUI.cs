using Raylib_cs;
using rlImGui_cs;
using ImGuiNET;
using System.Numerics;
using System.Diagnostics;
using Newtonsoft.Json;
using NES;

namespace Emulator
{
    public class GUI
    {
        // config
        private Config config;

        // emulator
        private bool runMainLoop = true;

        private const int RENDER_TARGET_FPS = 120;
        private const double MAX_CATCH_UP = 0.25;

        private NES.NES? nes
        {
            get => EmulatorState.NES;
            set => EmulatorState.NES = value;
        }
        
        private Audio audio;
        private Input input;
        private Renderer renderer;

        private readonly Stopwatch emulationClock = Stopwatch.StartNew();
        private double emulationTimeOwed = 0.0;

        // dialogs
        private GUI_About aboutWindow;
        private GUI_Remapper remapperWindow;
        private GUI_Manual manualWindow;
        private GUI_VideoSettings videoSettingsWindow;
        private GUI_GameGenie gameGenieWindow;

        private FileDialog fileDialog;
        private string selectedFilePath = "";

        Image icon;
        Texture2D backgroundTexture;

        public GUI(string[] args)
        {
            LoadConfig();
            ApplyConfigToState();
            EmulatorState.ParseStateArgs(args);

            if (!EmulatorState.raylibLog) Raylib.SetTraceLogLevel(TraceLogLevel.None);

            Raylib.SetConfigFlags(ConfigFlags.VSyncHint);
            Raylib.InitWindow(256 * EmulatorState.scale, 240 * EmulatorState.scale, "NES");
            Raylib.InitAudioDevice();

            Raylib.SetTargetFPS(RENDER_TARGET_FPS);
            Raylib.SetExitKey(KeyboardKey.Null);

            rlImGui.Setup();
            ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;

            // Init cheat manager
            CheatManager.Load();

            // Create systems
            audio = new Audio 
            {
                Volume = config.Volume
            };

            input = new Input
            {
                Bindings = config.Bindings,
                AltBindings = config.AltBindings,
                GamepadBindings = config.GamepadBindings,
                GamepadIndex = config.GamepadIndex,
                UseLeftStickAsDPad = config.UseLeftStickAsDPad
            };

            renderer = new Renderer
            {
                PaddingTop = config.PaddingTop,
                PaddingLeft = config.PaddingLeft,
                PaddingBottom = config.PaddingBottom,
                PaddingRight = config.PaddingRight,
            };

            // Create dialogs
            aboutWindow = new GUI_About();
            remapperWindow = new GUI_Remapper(input);
            manualWindow = new GUI_Manual();
            videoSettingsWindow = new GUI_VideoSettings(renderer);
            gameGenieWindow = new GUI_GameGenie();

            // Unsafe access for no imgui.ini files
            var io = ImGui.GetIO();
            unsafe
            {
                IntPtr ioPtr = (IntPtr)io.NativePtr;
                ImGuiIO* imguiIO = (ImGuiIO*)ioPtr.ToPointer();
                imguiIO->IniFilename = null;
            }

            fileDialog = new FileDialog(Directory.GetCurrentDirectory());

            icon = Raylib.LoadImage(Path.Combine(AppContext.BaseDirectory, "res", "Logo.png"));
            backgroundTexture = Raylib.LoadTexture(Path.Combine(AppContext.BaseDirectory, "res", "Background.png"));

            Raylib.SetWindowIcon(icon);
            runMainLoop = true;
        }

        private void ApplyConfigToState()
        {
            EmulatorState.scale = config.Scale;
            EmulatorState.tvSystemMode = config.TvSystemMode;
            EmulatorState.fpsEnable = config.DrawFPS;
        }

        private void SyncStateToConfig()
        {
            config.Scale = EmulatorState.scale;
            config.TvSystemMode = EmulatorState.tvSystemMode;
            config.DrawFPS = EmulatorState.fpsEnable;
            config.Volume = audio.Volume;
            config.Bindings = input.Bindings;
            config.AltBindings = input.AltBindings;
            config.GamepadBindings = input.GamepadBindings;
            config.GamepadIndex = input.GamepadIndex;
            config.UseLeftStickAsDPad = input.UseLeftStickAsDPad;
            config.PaddingRight = renderer.PaddingRight;
            config.PaddingLeft = renderer.PaddingLeft;
            config.PaddingTop = renderer.PaddingTop;
            config.PaddingBottom = renderer.PaddingBottom;
        }

        private void LoadConfig()
        {
            if (File.Exists("config.json"))
            {
                try
                {
                    var loadedConfig = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
                    if (loadedConfig == null)
                    {
                        config = new Config();
                        SaveConfig();
                    }
                    else
                    {
                        config = loadedConfig;
                    }
                }
                catch
                {
                    config = new Config();
                    SaveConfig();
                }
            }
            else
            {
                config = new Config();
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            File.WriteAllText("config.json", JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        private void QuickLoadState()
        {
            bool success = (nes != null) && SaveStateManager.LoadState(nes, SaveStateManager.QUICKSAVE_SLOT);
            if (success)
            {
                ToastManager.Show("Loaded quick save");
            }
            else
            {
                ToastManager.Show("Failed to load quick save");
            }
        }

        private void QuickSaveState()
        {
            if (nes != null)
            {
                SaveStateManager.SaveState(nes, SaveStateManager.QUICKSAVE_SLOT);
                ToastManager.Show("Quick save updated");
            }
        }

        private void LoadState(int slot)
        {
            bool success = (nes != null) && SaveStateManager.LoadState(nes, slot);
            if (success)
            {
                ToastManager.Show($"Loaded save state slot {slot}");
            }
            else
            {
                ToastManager.Show($"Failed to load save state slot {slot}");
            }
        }

        private void SaveState(int slot)
        {
            if (nes != null)
            {
                SaveStateManager.SaveState(nes, slot);
                ToastManager.Show($"Save state slot {slot} updated");
            }
        }

        private void OpenFileDialog()
        {
            if (OperatingSystem.IsWindows())
            {
                if (WindowsFileDialog.TryOpen(out string path, Directory.GetCurrentDirectory()))
                {
                    EmulatorState.romPath = path;
                    EmulatorState.insertingRom = true;
                }
            }
            else
            {
                fileDialog.Open();
            }
        }

        private void HandleInput()
        {
            var io = ImGui.GetIO();
            if (io.WantCaptureKeyboard)
            {
                // don't do things when imgui is asking
                return;
            }

            // Static inputs
            if (Raylib.IsKeyPressed(KeyboardKey.Space))
            {
                EmulatorState.showMenuBar = !EmulatorState.showMenuBar;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.P))
            {
                EmulatorState.paused = !EmulatorState.paused;
                if (!EmulatorState.paused)
                {
                    ToastManager.Show("Emulation resumed");
                    emulationClock.Restart();
                    emulationTimeOwed = 0.0;
                }
                else
                {
                    ToastManager.Show("Emulation paused");
                }
            }

            // Remappable inputs
            if (input.IsHotkeyPressed(EmulatorButton.QuickSaveState))
            {
                QuickSaveState();
            }
            if (input.IsHotkeyPressed(EmulatorButton.QuickLoadState))
            {
                QuickLoadState();
            }

            for (int i = 0; i < SaveStateManager.NUM_SLOTS; i++)
            {
                var checkKeyS = (EmulatorButton)((int)(EmulatorButton.SaveState0 + i));
                var checkKeyL = (EmulatorButton)((int)(EmulatorButton.LoadState0 + i));

                if (input.IsHotkeyPressed(checkKeyS))
                {
                    SaveState(i);
                }
                if (input.IsHotkeyPressed(checkKeyL))
                {
                    LoadState(i);
                }
            }
        }

        private void HandleControllerInput()
        {
            var io = ImGui.GetIO();
            if (nes == null || io.WantCaptureKeyboard)
            {
                // don't do things when imgui is asking
                return;
            }

            input.UpdateController();
            nes.Bus.Input.UpdateController(0, input.ControllerState);
        }

        private void LoadNewRom()
        {
            Cartridge cartridge;
            try
            {
                cartridge = new Cartridge(EmulatorState.romPath);
                Console.WriteLine(
                    $"Cartridge loaded: Mapper {cartridge.mapperID}, " +
                    $"PRG-ROM {cartridge.prgBanks * 16}KB, " +
                    $"{(cartridge.chrROM.Length > 0 ? $"{cartridge.chrBanks * 8}KB CHR-ROM" : "CHR-RAM")}, " +
                    $"TV System: {cartridge.tvSystem}"
                );
                ToastManager.Show($"Loaded ROM: {Path.GetFileName(EmulatorState.romPath)}");
            }
            catch (Exception e)
            {
                EmulatorState.insertingRom = false;
                ToastManager.Show($"ROM load failed: {e.Message}");
                return;
            }

            var tvSystem = cartridge.tvSystem;
            if (EmulatorState.tvSystemMode != TvSystemMode.Auto)
            {
                tvSystem = (EmulatorState.tvSystemMode == TvSystemMode.ForcePAL)
                           ? TvSystem.PAL : TvSystem.NTSC;
            }

            try
            {
                nes = new NES.NES(cartridge, tvSystem);
                audio.Reset();

                CheatManager.ActivateCheats(cartridge, out int numCheats);
                if(numCheats > 0)
                {
                    ToastManager.Show($"Ativated {numCheats} cheat(s)");
                }

                EmulatorState.insertingRom = false;
            }
            catch (Exception e)
            {
                ToastManager.Show($"Emulator init failed: {e.Message}");
                nes = null;
                EmulatorState.insertingRom = false;
            }
        }

        public void Run()
        {
            while (!Raylib.WindowShouldClose() && runMainLoop)
            {
                // Pre-update
                Raylib.SetWindowSize(256 * EmulatorState.scale, 240 * EmulatorState.scale);
                Raylib.BeginDrawing();
                rlImGui.Begin();
                GUI_Style.ApplyNesTheme();
                Raylib.ClearBackground(Raylib_cs.Color.Black);

                MenuBar();

                // Update
                if (EmulatorState.romPath.Length != 0 && 
                    EmulatorState.insertingRom == false &&
                    nes != null)
                {
                    if (!EmulatorState.paused)
                    {
                        double elapsedSeconds = emulationClock.Elapsed.TotalSeconds;
                        emulationClock.Restart();

                        // cap debt
                        emulationTimeOwed += Math.Min(elapsedSeconds, MAX_CATCH_UP);

                        double emulatedFrameDuration = 1.0 / nes.Timing.FrameRate;

                        // emulate until we've run enough frames
                        while (emulationTimeOwed >= emulatedFrameDuration)
                        {
                            HandleControllerInput();
                            nes.Run();
                            audio.AddSamples(nes.Bus.APU.GetSamples());
                            emulationTimeOwed -= emulatedFrameDuration;
                        }
                    }

                    renderer.DrawFrame(nes.Bus.PPU, EmulatorState.scale);
                    audio.Update();
                }
                else if (EmulatorState.insertingRom == true)
                {
                    LoadNewRom();
                }
                else
                {
                    Raylib.ClearBackground(Raylib_cs.Color.DarkGray);
                    Raylib.DrawTextureEx(backgroundTexture, new Vector2(0, -5), 0, (float)(EmulatorState.scale * 0.50), Raylib_cs.Color.White);
                }

                // Post-update
                HandleInput();

                if (EmulatorState.fpsEnable) Raylib.DrawFPS(0, EmulatorState.showMenuBar ? 19 : 0);
                ToastManager.Draw();

                rlImGui.End();
                Raylib.EndDrawing();
            }

            SyncStateToConfig();
            SaveConfig();

            audio.Dispose();
            Raylib.CloseAudioDevice();
            Raylib.CloseWindow();
        }

        private void DrawRegionMenuItem(string label, TvSystemMode mode)
        {
            if (ImGui.MenuItem(label, null, EmulatorState.tvSystemMode == mode))
            {
                if (EmulatorState.tvSystemMode != mode)
                {
                    EmulatorState.tvSystemMode = mode;
                    if (EmulatorState.romPath.Length != 0)
                    {
                        EmulatorState.insertingRom = true;
                    }
                    ToastManager.Show($"Region switched to: {mode}");
                }
            }
        }

        public void MenuBar()
        {
            if (EmulatorState.showMenuBar)
            {
                if (ImGui.BeginMainMenuBar())
                {
                    ImGui.Text("NET-NES");

                    ImGui.Separator();

                    if (ImGui.BeginMenu("File"))
                    {
                        if (ImGui.MenuItem("Reset"))
                        {
                            nes?.Reset();
                        }
                        if (ImGui.MenuItem("Open ROM"))
                        {
                            OpenFileDialog();
                            EmulatorState.showMenuBar = false;
                        }
                        ImGui.Separator();
                        if (ImGui.MenuItem("Quick Load State"))
                        {
                            QuickLoadState();
                        }
                        if (ImGui.MenuItem("Quick Save State"))
                        {
                            QuickSaveState();
                        }
                        if (ImGui.BeginMenu("Load State"))
                        {
                            for (int i = 0; i < SaveStateManager.NUM_SLOTS; i++)
                            {
                                if (ImGui.MenuItem($"Slot {i}"))
                                {
                                    LoadState(i);
                                }
                            }
                            ImGui.EndMenu();
                        }
                        if (ImGui.BeginMenu("Save State"))
                        {
                            for (int i = 0; i < SaveStateManager.NUM_SLOTS; i++)
                            {
                                if (ImGui.MenuItem($"Slot {i}"))
                                {
                                    SaveState(i);
                                }
                            }
                            ImGui.EndMenu();
                        }
                        ImGui.Separator();
                        if (ImGui.MenuItem("Exit"))
                        {
                            runMainLoop = false;
                        }
                        ImGui.EndMenu();
                    }
                    if(ImGui.BeginMenu("Game"))
                    {
                        if(ImGui.MenuItem("Cheats"))
                        {
                            gameGenieWindow.Showing = true;
                        }
                        ImGui.EndMenu();
                    }
                    if (ImGui.BeginMenu("Config"))
                    {
                        if (ImGui.MenuItem("Controls"))
                        {
                            remapperWindow.Showing = true;
                        }
                        if (ImGui.BeginMenu("Audio"))
                        {
                            float vol = (float)audio.Volume;
                            if (ImGui.SliderFloat("Volume", ref vol, 0.0f, 1.0f))
                            {
                                audio.Volume = vol;
                            }

                            ImGui.Separator();
                            ImGui.EndMenu();
                        }
                        if (ImGui.MenuItem("Video"))
                        {
                            videoSettingsWindow.Showing = true;
                        }
                        if (ImGui.BeginMenu("Region"))
                        {
                            DrawRegionMenuItem("Auto (from ROM header)", TvSystemMode.Auto);
                            DrawRegionMenuItem("Force NTSC", TvSystemMode.ForceNTSC);
                            DrawRegionMenuItem("Force PAL", TvSystemMode.ForcePAL);

                            if (nes != null)
                            {
                                ImGui.Separator();
                                ImGui.TextDisabled("Current ROM: " + nes.Timing.System);
                            }
                            ImGui.EndMenu();
                        }

                        ImGui.Separator();
                        if(ImGui.BeginMenu("Fun Stuff"))
                        {
                            if (ImGui.MenuItem("Enable Broken CPU", null, nes?.Bus.CPU.EnableGlitches ?? false))
                            {
                                if(nes != null)
                                {
                                    nes.Bus.CPU.EnableGlitches = !nes.Bus.CPU.EnableGlitches;
                                }
                            }
                            ImGui.EndMenu();
                        }
                        ImGui.EndMenu();
                    }
                    if (ImGui.BeginMenu("Debug"))
                    {
                        if (ImGui.MenuItem("FPS Enable", null, EmulatorState.fpsEnable))
                        {
                            EmulatorState.fpsEnable = !EmulatorState.fpsEnable;
                        }
                        if (ImGui.MenuItem("Sprite0 Hit Disable", null, nes?.Bus.PPU.SkipSprite0HitCheck ?? false))
                        {
                            if (nes != null)
                            {
                                var ppu = nes.Bus.PPU;
                                ppu.SkipSprite0HitCheck = !ppu.SkipSprite0HitCheck;
                                ToastManager.Show($"Sprite0 Hit Check Disable: {ppu.SkipSprite0HitCheck}");
                            }
                        }
                        if (ImGui.BeginMenu("Sound"))
                        {
                            if (nes == null)
                            {
                                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Hardware not initialized");
                            }
                            else
                            {
                                var apu = nes.Bus.APU;
                                if (ImGui.MenuItem("Enable IRQ", null, apu.EnableIRQ))
                                {
                                    apu.EnableIRQ = !apu.EnableIRQ;
                                }
                                ImGui.Separator();

                                ImGui.Text("Enabled Channels:");
                                bool pulse1 = (apu.EnabledSoundChannels & APUChannels.Pulse1) != 0;
                                bool pulse2 = (apu.EnabledSoundChannels & APUChannels.Pulse2) != 0;
                                bool triangle = (apu.EnabledSoundChannels & APUChannels.Triangle) != 0;
                                bool noise = (apu.EnabledSoundChannels & APUChannels.Noise) != 0;
                                bool dmc = (apu.EnabledSoundChannels & APUChannels.Dmc) != 0;

                                if (ImGui.MenuItem("Pulse 1", "", pulse1))
                                    apu.EnabledSoundChannels ^= APUChannels.Pulse1;

                                if (ImGui.MenuItem("Pulse 2", "", pulse2))
                                    apu.EnabledSoundChannels ^= APUChannels.Pulse2;

                                if (ImGui.MenuItem("Triangle", "", triangle))
                                    apu.EnabledSoundChannels ^= APUChannels.Triangle;

                                if (ImGui.MenuItem("Noise", "", noise))
                                    apu.EnabledSoundChannels ^= APUChannels.Noise;

                                if (ImGui.MenuItem("DMC", "", dmc))
                                    apu.EnabledSoundChannels ^= APUChannels.Dmc;

                                ImGui.Separator();

                                bool all = apu.EnabledSoundChannels == APUChannels.All;

                                if (ImGui.MenuItem("All", "", all))
                                    apu.EnabledSoundChannels = all
                                        ? 0
                                        : APUChannels.All;
                            }

                            ImGui.EndMenu();
                        }
                        ImGui.EndMenu();
                    }
                    if (ImGui.BeginMenu("Help"))
                    {
                        if (ImGui.MenuItem("Manual"))
                        {
                            manualWindow.Showing = true;
                        }
                        if (ImGui.MenuItem("About"))
                        {
                            aboutWindow.Showing = true;
                        }
                        ImGui.EndMenu();
                    }
                }
            }
            else
            {
                EmulatorState.showMenuBar = ImGui.GetMousePos().Y <= 20.0f && ImGui.GetMousePos().Y != 0;
            }

            ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, 0), ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(256 * EmulatorState.scale, 240 * EmulatorState.scale), ImGuiCond.Appearing);
            if (fileDialog.Show(ref selectedFilePath))
            {
                EmulatorState.romPath = selectedFilePath;
                EmulatorState.insertingRom = true;
            }

            if (videoSettingsWindow.Showing) videoSettingsWindow.Draw();
            if (manualWindow.Showing) manualWindow.Draw();
            if (aboutWindow.Showing) aboutWindow.Draw();
            if (remapperWindow.Showing) remapperWindow.Draw();
            if (gameGenieWindow.Showing) gameGenieWindow.Draw();
        }
    }
}