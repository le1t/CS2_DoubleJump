using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Commands;
using System.Text.Json.Serialization;

namespace CS2DoubleJump;

public class DoubleJumpConfig : BasePluginConfig
{
    [JsonPropertyName("css_doublejump_boost_units")]
    public float Boost { get; set; } = 300.0f;
    
    [JsonPropertyName("css_doublejump_max_jumps")]
    public int MaxJumps { get; set; } = 1;
    
    [JsonPropertyName("css_doublejump_enabled")]
    public bool Enabled { get; set; } = true;
    
    // Новые параметры для прыжка в воздухе
    [JsonPropertyName("css_doublejump_air_jump_enabled")]
    public bool AirJumpEnabled { get; set; } = true;
    
    [JsonPropertyName("css_doublejump_min_velocity_for_air_jump")]
    public float MinVelocityForAirJump { get; set; } = -50.0f;
    
    [JsonPropertyName("css_doublejump_air_jump_boost_multiplier")]
    public float AirJumpBoostMultiplier { get; set; } = 1.0f;
}

[MinimumApiVersion(362)]
public class DoubleJump : BasePlugin, IPluginConfig<DoubleJumpConfig>
{
    public override string ModuleName => "CS2 DoubleJump";
    public override string ModuleVersion => "1.5";
    public override string ModuleAuthor => "Ported by le1t1337 + AI DeepSeek. Сode logic by darkranger";

    private readonly Dictionary<int, int> _playerJumps = new();
    private readonly Dictionary<int, bool> _wasOnGround = new();
    private readonly Dictionary<int, bool> _lastJumpPressed = new();
    private readonly Dictionary<int, float> _lastZVelocity = new();
    private readonly Dictionary<int, bool> _didGroundJump = new();
    
    public required DoubleJumpConfig Config { get; set; }
    private const uint FL_ONGROUND = (uint)PlayerFlags.FL_ONGROUND;

    public void OnConfigParsed(DoubleJumpConfig config)
    {
        Config = config;
        
        // Валидация конфигурации
        if (Config.Boost < 0) Config.Boost = 300.0f;
        if (Config.MaxJumps < 1) Config.MaxJumps = 1;
        if (Config.MaxJumps > 5) Config.MaxJumps = 5;
    }

    public override void Load(bool hotReload)
    {
        // Регистрируем команды
        AddCommand("css_doublejump_help", "Show Double Jump help", OnHelpCommand);
        AddCommand("css_doublejump_settings", "Show current Double Jump settings", OnSettingsCommand);
        
        // Выводим информацию о конфигурации
        PrintConVarInfo();
        
        // Регистрируем обработчики событий
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterListener<Listeners.OnTick>(OnTick);
        
        // Инициализируем данные для всех текущих игроков
        Server.NextFrame(() =>
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player != null && player.IsValid && !player.IsBot)
                {
                    int slot = player.Slot;
                    _playerJumps[slot] = 0;
                    _lastZVelocity[slot] = 0.0f;
                    _didGroundJump[slot] = false;
                    _wasOnGround[slot] = true;
                    _lastJumpPressed[slot] = false;
                }
            }
        });
        
        // Регистрируем обработчик подключения игрока
        RegisterEventHandler<EventPlayerConnect>(OnPlayerConnect);
        
        if (hotReload)
        {
            Server.NextFrame(() =>
            {
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player != null && player.IsValid && !player.IsBot)
                    {
                        int slot = player.Slot;
                        _playerJumps[slot] = 0;
                        _lastZVelocity[slot] = 0.0f;
                        _didGroundJump[slot] = false;
                        _wasOnGround[slot] = true;
                        _lastJumpPressed[slot] = false;
                    }
                }
            });
        }
    }

    private void PrintConVarInfo()
    {
        Console.WriteLine("===============================================");
        Console.WriteLine("[DoubleJump] Plugin successfully loaded!");
        Console.WriteLine($"[DoubleJump] Version: {ModuleVersion}");
        Console.WriteLine($"[DoubleJump] Minimum API Version: 362");
        Console.WriteLine("[DoubleJump] Current settings:");
        Console.WriteLine($"[DoubleJump]   css_doublejump_enabled = {Config.Enabled}");
        Console.WriteLine($"[DoubleJump]   css_doublejump_boost_units = {Config.Boost}");
        Console.WriteLine($"[DoubleJump]   css_doublejump_max_jumps = {Config.MaxJumps}");
        Console.WriteLine($"[DoubleJump]   css_doublejump_air_jump_enabled = {Config.AirJumpEnabled}");
        Console.WriteLine($"[DoubleJump]   css_doublejump_min_velocity_for_air_jump = {Config.MinVelocityForAirJump}");
        Console.WriteLine($"[DoubleJump]   css_doublejump_air_jump_boost_multiplier = {Config.AirJumpBoostMultiplier}");
        Console.WriteLine("===============================================");
    }

    private HookResult OnPlayerConnect(EventPlayerConnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid && !player.IsBot)
        {
            int slot = player.Slot;
            _playerJumps[slot] = 0;
            _wasOnGround[slot] = true;
            _lastJumpPressed[slot] = false;
            _lastZVelocity[slot] = 0.0f;
            _didGroundJump[slot] = false;
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            int slot = player.Slot;
            _playerJumps[slot] = 0;
            _wasOnGround[slot] = true;
            _lastJumpPressed[slot] = false;
            _lastZVelocity[slot] = 0.0f;
            _didGroundJump[slot] = false;
        }
        return HookResult.Continue;
    }

    private void OnTick()
    {
        if (!Config.Enabled)
            return;

        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive || player.IsBot)
                continue;

            ProcessDoubleJump(player);
        }
    }

    private void ProcessDoubleJump(CCSPlayerController player)
    {
        int slot = player.Slot;
        var pawn = player.PlayerPawn?.Value;
        
        if (pawn == null || !pawn.IsValid)
            return;

        // Используем битовую операцию для проверки флага FL_ONGROUND
        bool isOnGround = (pawn.Flags & FL_ONGROUND) == FL_ONGROUND;
        bool jumpPressed = (player.Buttons & PlayerButtons.Jump) != 0;
        var velocity = pawn.AbsVelocity;
        float currentZVelocity = velocity.Z;
        
        // Обновляем вертикальную скорость
        _lastZVelocity[slot] = currentZVelocity;
        
        // Обработка прыжка с земли
        if (_wasOnGround.GetValueOrDefault(slot, true) && !isOnGround && jumpPressed && !_lastJumpPressed.GetValueOrDefault(slot, false))
        {
            _playerJumps[slot] = 1;
            _didGroundJump[slot] = true;
        }
        else if (isOnGround)
        {
            // Сброс при приземлении
            _playerJumps[slot] = 0;
            _didGroundJump[slot] = false;
        }
        else if (!isOnGround && jumpPressed && !_lastJumpPressed.GetValueOrDefault(slot, false))
        {
            // Проверяем возможность прыжка в воздухе
            bool canAirJump = false;
            
            if (Config.AirJumpEnabled && _playerJumps.TryGetValue(slot, out var jumps))
            {
                // Если игрок уже сделал прыжок с земли
                if (jumps > 0 && jumps <= Config.MaxJumps)
                {
                    canAirJump = true;
                }
                // Если игрок не делал прычок с земли, проверяем падение
                else if (jumps == 0 && CanAirJump(slot, currentZVelocity))
                {
                    canAirJump = true;
                }
            }
            
            if (canAirJump)
            {
                // Определяем тип прыжка и применяем соответствующий буст
                if (_didGroundJump[slot])
                {
                    // Обычный двойной прыжок после прыжка с земли
                    DoGroundJump(pawn);
                }
                else
                {
                    // Прыжок в воздухе при падении
                    DoAirJump(pawn, currentZVelocity);
                }
                
                _playerJumps[slot] = (_playerJumps.TryGetValue(slot, out var currentJumps) ? currentJumps : 0) + 1;
            }
        }
        
        _wasOnGround[slot] = isOnGround;
        _lastJumpPressed[slot] = jumpPressed;
    }

    private bool CanAirJump(int slot, float currentZVelocity)
    {
        float lastZVelocity = _lastZVelocity.GetValueOrDefault(slot, 0.0f);
        
        bool isFalling = currentZVelocity < 0;
        bool hasMinVelocity = Math.Abs(currentZVelocity) > Math.Abs(Config.MinVelocityForAirJump);
        bool notRisingTooFast = currentZVelocity < 100.0f;
        
        return isFalling && hasMinVelocity && notRisingTooFast;
    }

    private void DoGroundJump(CBasePlayerPawn pawn)
    {
        var velocity = pawn.AbsVelocity;
        velocity.Z = Config.Boost;
        pawn.Teleport(null, null, velocity);
    }

    private void DoAirJump(CBasePlayerPawn pawn, float currentZVelocity)
    {
        var velocity = pawn.AbsVelocity;
        float boostMultiplier = Config.AirJumpBoostMultiplier;
        
        if (currentZVelocity < -200.0f)
        {
            boostMultiplier *= 1.2f;
        }
        else if (currentZVelocity < -100.0f)
        {
            boostMultiplier *= 1.1f;
        }
        
        velocity.Z = Config.Boost * boostMultiplier;
        velocity.X *= 1.05f;
        velocity.Y *= 1.05f;
        
        pawn.Teleport(null, null, velocity);
    }

    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        string helpMessage = """
            ===============================================
            DOUBLE JUMP PLUGIN HELP
            ===============================================
            DESCRIPTION:
              Allows players to perform double jumps in CS2.
              Now with air jump functionality for falling!

            COMMANDS:
              css_doublejump_help - Show this help message
              css_doublejump_settings - Show current plugin settings
              css_plugins reload CS2DoubleJump - Reload configuration file
            ===============================================
            """;
        
        if (player != null)
        {
            player.PrintToConsole(helpMessage);
            player.PrintToChat($"Double Jump v{ModuleVersion}: Check console for help");
        }
        else
        {
            Console.WriteLine(helpMessage);
        }
    }

    private void OnSettingsCommand(CCSPlayerController? player, CommandInfo command)
    {
        string settingsMessage = $"""
            ===============================================
            DOUBLE JUMP CURRENT SETTINGS
            ===============================================
            Plugin Enabled: {Config.Enabled}
            Air Jump Enabled: {Config.AirJumpEnabled}
            Boost Height: {Config.Boost} units
            Max Extra Jumps: {Config.MaxJumps}
            Min Air Velocity: {Config.MinVelocityForAirJump}
            Air Jump Boost Multiplier: {Config.AirJumpBoostMultiplier}
            ===============================================
            """;
        
        if (player != null)
        {
            player.PrintToConsole(settingsMessage);
            player.PrintToChat($"Double Jump: Enabled={Config.Enabled}, AirJump={Config.AirJumpEnabled}");
        }
        else
        {
            Console.WriteLine(settingsMessage);
        }
    }
}