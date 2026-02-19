using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json.Serialization;

namespace CS2DoubleJump;

public class DoubleJumpConfig : BasePluginConfig
{
    /// <summary>
    /// Включить/выключить плагин (по умолчанию: 1)
    /// 0 - выключен, 1 - включен
    /// </summary>
    [JsonPropertyName("css_doublejump_enabled")]
    public int Enabled { get; set; } = 1;

    /// <summary>
    /// Высота дополнительного прыжка (по умолчанию: 300.0)
    /// Диапазон: >= 0
    /// </summary>
    [JsonPropertyName("css_doublejump_boost_units")]
    public float Boost { get; set; } = 300.0f;

    /// <summary>
    /// Максимальное количество дополнительных прыжков (по умолчанию: 1)
    /// Диапазон: 1-5
    /// </summary>
    [JsonPropertyName("css_doublejump_max_jumps")]
    public int MaxJumps { get; set; } = 1;

    /// <summary>
    /// Разрешить прыжок в воздухе при падении (по умолчанию: 1)
    /// 0 - запрещён, 1 - разрешён
    /// </summary>
    [JsonPropertyName("css_doublejump_air_jump_enabled")]
    public int AirJumpEnabled { get; set; } = 1;

    /// <summary>
    /// Минимальная вертикальная скорость для air jump (отрицательная) (по умолчанию: -50.0)
    /// Диапазон: <= 0
    /// </summary>
    [JsonPropertyName("css_doublejump_min_velocity_for_air_jump")]
    public float MinVelocityForAirJump { get; set; } = -50.0f;

    /// <summary>
    /// Множитель высоты air jump (по умолчанию: 1.0)
    /// Диапазон: >= 0
    /// </summary>
    [JsonPropertyName("css_doublejump_air_jump_boost_multiplier")]
    public float AirJumpBoostMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Уровень логирования (по умолчанию: 4 - Error)
    /// 0 - Trace
    /// 1 - Debug
    /// 2 - Information
    /// 3 - Warning
    /// 4 - Error
    /// 5 - Critical
    /// </summary>
    [JsonPropertyName("css_doublejump_loglevel")]
    public int LogLevel { get; set; } = 4;
}

[MinimumApiVersion(362)]
public class CS2_DoubleJump : BasePlugin, IPluginConfig<DoubleJumpConfig>
{
    public override string ModuleName => "CS2 DoubleJump";
    public override string ModuleVersion => "1.7";
    public override string ModuleAuthor => "Fixed by le1t1337 + AI DeepSeek. Code logic by darkranger";

    private readonly Dictionary<int, int> _playerJumps = new();
    private readonly Dictionary<int, bool> _wasOnGround = new();
    private readonly Dictionary<int, bool> _lastJumpPressed = new();
    private readonly Dictionary<int, float> _lastZVelocity = new();
    private readonly Dictionary<int, bool> _didGroundJump = new();

    public required DoubleJumpConfig Config { get; set; }
    private const uint FL_ONGROUND = (uint)PlayerFlags.FL_ONGROUND;

    // Логирование с учётом уровня
    private void Log(LogLevel level, string message)
    {
        if ((int)level >= Config.LogLevel)
        {
            switch (level)
            {
                case LogLevel.Trace: Logger.LogTrace(message); break;
                case LogLevel.Debug: Logger.LogDebug(message); break;
                case LogLevel.Information: Logger.LogInformation(message); break;
                case LogLevel.Warning: Logger.LogWarning(message); break;
                case LogLevel.Error: Logger.LogError(message); break;
                case LogLevel.Critical: Logger.LogCritical(message); break;
            }
        }
    }

    public void OnConfigParsed(DoubleJumpConfig config)
    {
        // Валидация числовых параметров
        config.Boost = Math.Max(0, config.Boost);
        config.MaxJumps = Math.Clamp(config.MaxJumps, 1, 5);
        config.AirJumpBoostMultiplier = Math.Max(0, config.AirJumpBoostMultiplier);
        config.MinVelocityForAirJump = Math.Min(0, config.MinVelocityForAirJump); // должно быть отрицательным или нулём
        config.Enabled = Math.Clamp(config.Enabled, 0, 1);
        config.AirJumpEnabled = Math.Clamp(config.AirJumpEnabled, 0, 1);
        config.LogLevel = Math.Clamp(config.LogLevel, 0, 5);

        Config = config;

        // Удаление старого конфига, если он есть (путь counterstrikesharp\plugins\...)
        try
        {
            string oldConfigPath = Path.Combine(Server.GameDirectory, "counterstrikesharp", "plugins", "CS2_DoubleJump.json");
            if (File.Exists(oldConfigPath))
            {
                File.Delete(oldConfigPath);
                Log(LogLevel.Information, $"Старый конфиг удалён: {oldConfigPath}");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Warning, $"Не удалось удалить старый конфиг: {ex.Message}");
        }
    }

    public override void Load(bool hotReload)
    {
        // Регистрация команд
        AddCommand("css_doublejump_help", "Показать справку по плагину DoubleJump", OnHelpCommand);
        AddCommand("css_doublejump_settings", "Показать текущие настройки плагина", OnSettingsCommand);
        AddCommand("css_doublejump_test", "Тестовая команда для проверки", OnTestCommand);
        AddCommand("css_doublejump_reload", "Перезагрузить конфигурацию плагина", OnReloadCommand);

        AddCommand("css_doublejump_setenabled", "Включить/выключить плагин (0/1)", OnSetEnabledCommand);
        AddCommand("css_doublejump_setairjumpenabled", "Включить/выключить air jump (0/1)", OnSetAirJumpEnabledCommand);
        AddCommand("css_doublejump_setboost", "Установить высоту доп. прыжка (число с плавающей точкой)", OnSetBoostCommand);
        AddCommand("css_doublejump_setmaxjumps", "Установить макс. кол-во доп. прыжков (1-5)", OnSetMaxJumpsCommand);
        AddCommand("css_doublejump_setminvelocity", "Установить мин. скорость для air jump (отрицательное число)", OnSetMinVelocityCommand);
        AddCommand("css_doublejump_setairjumpmultiplier", "Установить множитель высоты air jump (>=0)", OnSetAirJumpMultiplierCommand);
        AddCommand("css_doublejump_setloglevel", "Установить уровень логирования (0-5)", OnSetLogLevelCommand);

        // Регистрация событий
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterListener<Listeners.OnTick>(OnTick);

        // Инициализация данных для текущих игроков (включая ботов)
        Server.NextFrame(() =>
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player != null && player.IsValid && player.PawnIsAlive)
                {
                    int slot = player.Slot;
                    ResetPlayerData(slot);
                }
            }
        });

        if (hotReload)
        {
            Server.NextFrame(() =>
            {
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player != null && player.IsValid && player.PawnIsAlive)
                    {
                        int slot = player.Slot;
                        ResetPlayerData(slot);
                    }
                }
            });
        }

        PrintInfo();
    }

    private void ResetPlayerData(int slot)
    {
        _playerJumps[slot] = 0;
        _lastZVelocity[slot] = 0.0f;
        _didGroundJump[slot] = false;
        _wasOnGround[slot] = true;
        _lastJumpPressed[slot] = false;
    }

    private void PrintInfo()
    {
        Log(LogLevel.Information, "===============================================");
        Log(LogLevel.Information, $"[{ModuleName}] Плагин успешно загружен!");
        Log(LogLevel.Information, $"[{ModuleName}] Версия: {ModuleVersion}");
        Log(LogLevel.Information, $"[{ModuleName}] Автор: {ModuleAuthor}");
        Log(LogLevel.Information, $"[{ModuleName}] Текущие настройки:");
        Log(LogLevel.Information, $"[{ModuleName}]   css_doublejump_enabled = {Config.Enabled}");
        Log(LogLevel.Information, $"[{ModuleName}]   css_doublejump_boost_units = {Config.Boost}");
        Log(LogLevel.Information, $"[{ModuleName}]   css_doublejump_max_jumps = {Config.MaxJumps}");
        Log(LogLevel.Information, $"[{ModuleName}]   css_doublejump_air_jump_enabled = {Config.AirJumpEnabled}");
        Log(LogLevel.Information, $"[{ModuleName}]   css_doublejump_min_velocity_for_air_jump = {Config.MinVelocityForAirJump}");
        Log(LogLevel.Information, $"[{ModuleName}]   css_doublejump_air_jump_boost_multiplier = {Config.AirJumpBoostMultiplier}");
        Log(LogLevel.Information, $"[{ModuleName}]   css_doublejump_loglevel = {Config.LogLevel}");
        Log(LogLevel.Information, "===============================================");
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            int slot = player.Slot;
            ResetPlayerData(slot);
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            int slot = player.Slot;
            _playerJumps.Remove(slot);
            _wasOnGround.Remove(slot);
            _lastJumpPressed.Remove(slot);
            _lastZVelocity.Remove(slot);
            _didGroundJump.Remove(slot);
        }
        return HookResult.Continue;
    }

    private void OnTick()
    {
        if (Config.Enabled == 0)
            return;

        foreach (var player in Utilities.GetPlayers())
        {
            // Проверка на жизнь обязательна, иначе pawn может быть недействительным.
            // Боты имеют PawnIsAlive == true, когда живы, поэтому они обрабатываются.
            if (player == null || !player.IsValid || !player.PawnIsAlive)
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

        bool isOnGround = (pawn.Flags & FL_ONGROUND) == FL_ONGROUND;
        bool jumpPressed = (player.Buttons & PlayerButtons.Jump) != 0;
        var velocity = pawn.AbsVelocity;
        float currentZVelocity = velocity.Z;

        _lastZVelocity[slot] = currentZVelocity;

        // Прыжок с земли
        if (_wasOnGround.GetValueOrDefault(slot, true) && !isOnGround && jumpPressed && !_lastJumpPressed.GetValueOrDefault(slot, false))
        {
            _playerJumps[slot] = 1;
            _didGroundJump[slot] = true;
        }
        else if (isOnGround)
        {
            _playerJumps[slot] = 0;
            _didGroundJump[slot] = false;
        }
        else if (!isOnGround && jumpPressed && !_lastJumpPressed.GetValueOrDefault(slot, false))
        {
            bool canAirJump = false;

            if (Config.AirJumpEnabled == 1 && _playerJumps.TryGetValue(slot, out var jumps))
            {
                if (jumps > 0 && jumps <= Config.MaxJumps)
                {
                    canAirJump = true;
                }
                else if (jumps == 0 && CanAirJump(slot, currentZVelocity))
                {
                    canAirJump = true;
                }
            }

            if (canAirJump)
            {
                if (_didGroundJump[slot])
                {
                    DoGroundJump(pawn);
                }
                else
                {
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

    // --- Команды ---

    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        string help = $"""
            ================================================
            СПРАВКА ПО ПЛАГИНУ DOUBLE JUMP v{ModuleVersion}
            ================================================
            ОПИСАНИЕ:
              Позволяет игрокам выполнять двойные прыжки в CS2.
              Поддерживается прыжок в воздухе при падении.
              Боты также могут использовать двойной прыжок (если управление позволяет).

            КОМАНДЫ:
              css_doublejump_help                          - показать эту справку
              css_doublejump_settings                       - показать текущие настройки
              css_doublejump_test                            - тест (для игрока: данные о прыжках)
              css_doublejump_reload                          - перезагрузить конфиг

              css_doublejump_setenabled <0/1>                - вкл/выкл плагин
              css_doublejump_setairjumpenabled <0/1>         - вкл/выкл air jump
              css_doublejump_setboost <число>                 - высота доп. прыжка (>=0)
              css_doublejump_setmaxjumps <1-5>                - макс. кол-во доп. прыжков
              css_doublejump_setminvelocity <число>           - мин. скорость для air jump (<=0)
              css_doublejump_setairjumpmultiplier <число>     - множитель высоты air jump (>=0)
              css_doublejump_setloglevel <0-5>                - уровень логирования

            ПРИМЕРЫ:
              css_doublejump_setenabled 1
              css_doublejump_setboost 350.0
              css_doublejump_setmaxjumps 2
            ================================================
            """;
        command.ReplyToCommand(help);
        if (player != null)
            player.PrintToChat($" {ChatColors.Green}[DoubleJump]{ChatColors.Default} Справка в консоли.");
    }

    private void OnSettingsCommand(CCSPlayerController? player, CommandInfo command)
    {
        int activePlayers = Utilities.GetPlayers().Count(p => p != null && p.IsValid && p.PawnIsAlive);
        string settings = $"""
            ================================================
            ТЕКУЩИЕ НАСТРОЙКИ DOUBLE JUMP v{ModuleVersion}
            ================================================
            Плагин включен: {Config.Enabled} (по умолч. 1)
            Air jump включен: {Config.AirJumpEnabled} (по умолч. 1)
            Высота доп. прыжка: {Config.Boost.ToString(CultureInfo.InvariantCulture)} (по умолч. 300.0)
            Макс. доп. прыжков: {Config.MaxJumps} (по умолч. 1)
            Мин. скорость для air jump: {Config.MinVelocityForAirJump.ToString(CultureInfo.InvariantCulture)} (по умолч. -50.0)
            Множитель air jump: {Config.AirJumpBoostMultiplier.ToString(CultureInfo.InvariantCulture)} (по умолч. 1.0)
            Уровень логирования: {Config.LogLevel} (0-Trace,1-Debug,2-Info,3-Warning,4-Error,5-Critical)
            Активных игроков: {activePlayers}
            ================================================
            """;
        command.ReplyToCommand(settings);
        if (player != null)
            player.PrintToChat($" {ChatColors.Green}[DoubleJump]{ChatColors.Default} Настройки в консоли.");
    }

    private void OnTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null)
        {
            command.ReplyToCommand($"[DoubleJump] Тест: плагин работает. Используйте команду от имени игрока для детальной информации.");
            return;
        }

        if (!player.IsValid || !player.PawnIsAlive)
        {
            command.ReplyToCommand($"[DoubleJump] Вы не живы или невалидны.");
            return;
        }

        int slot = player.Slot;
        int jumps = _playerJumps.GetValueOrDefault(slot, 0);
        bool onGround = _wasOnGround.GetValueOrDefault(slot, true);
        float zVel = _lastZVelocity.GetValueOrDefault(slot, 0);
        command.ReplyToCommand($"[DoubleJump] Slot={slot}, Jumps={jumps}, OnGround={onGround}, Z={zVel:F2}");
    }

    private void OnReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        _playerJumps.Clear();
        _wasOnGround.Clear();
        _lastJumpPressed.Clear();
        _lastZVelocity.Clear();
        _didGroundJump.Clear();

        Server.NextFrame(() =>
        {
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && p.PawnIsAlive)
                {
                    ResetPlayerData(p.Slot);
                }
            }
        });

        command.ReplyToCommand($"[DoubleJump] Конфигурация перезагружена, временные данные сброшены.");
        Log(LogLevel.Information, $"Конфигурация перезагружена по команде.");
    }

    private void OnSetEnabledCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[DoubleJump] Текущее значение Enabled: {Config.Enabled} (по умолч. 1). Использование: css_doublejump_setenabled <0/1>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int value) && (value == 0 || value == 1))
        {
            int old = Config.Enabled;
            Config.Enabled = value;
            SaveConfig();
            command.ReplyToCommand($"[DoubleJump] Enabled изменён с {old} на {value}.");
            Log(LogLevel.Information, $"Enabled изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand($"[DoubleJump] Неверное значение. Используйте 0 или 1.");
        }
    }

    private void OnSetAirJumpEnabledCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[DoubleJump] Текущее значение AirJumpEnabled: {Config.AirJumpEnabled} (по умолч. 1). Использование: css_doublejump_setairjumpenabled <0/1>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int value) && (value == 0 || value == 1))
        {
            int old = Config.AirJumpEnabled;
            Config.AirJumpEnabled = value;
            SaveConfig();
            command.ReplyToCommand($"[DoubleJump] AirJumpEnabled изменён с {old} на {value}.");
            Log(LogLevel.Information, $"AirJumpEnabled изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand($"[DoubleJump] Неверное значение. Используйте 0 или 1.");
        }
    }

    private void OnSetBoostCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[DoubleJump] Текущее значение Boost: {Config.Boost.ToString(CultureInfo.InvariantCulture)} (по умолч. 300.0). Использование: css_doublejump_setboost <число>");
            return;
        }

        string arg = command.GetArg(1).Replace(',', '.');
        if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value >= 0)
        {
            float old = Config.Boost;
            Config.Boost = value;
            SaveConfig();
            command.ReplyToCommand($"[DoubleJump] Boost изменён с {old.ToString(CultureInfo.InvariantCulture)} на {value.ToString(CultureInfo.InvariantCulture)}.");
            Log(LogLevel.Information, $"Boost изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand($"[DoubleJump] Неверное значение. Введите неотрицательное число (можно с точкой).");
        }
    }

    private void OnSetMaxJumpsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[DoubleJump] Текущее значение MaxJumps: {Config.MaxJumps} (по умолч. 1, допустимо 1-5). Использование: css_doublejump_setmaxjumps <1-5>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int value) && value >= 1 && value <= 5)
        {
            int old = Config.MaxJumps;
            Config.MaxJumps = value;
            SaveConfig();
            command.ReplyToCommand($"[DoubleJump] MaxJumps изменён с {old} на {value}.");
            Log(LogLevel.Information, $"MaxJumps изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand($"[DoubleJump] Неверное значение. Используйте целое число от 1 до 5.");
        }
    }

    private void OnSetMinVelocityCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[DoubleJump] Текущее значение MinVelocityForAirJump: {Config.MinVelocityForAirJump.ToString(CultureInfo.InvariantCulture)} (по умолч. -50.0). Использование: css_doublejump_setminvelocity <число <=0>");
            return;
        }

        string arg = command.GetArg(1).Replace(',', '.');
        if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value <= 0)
        {
            float old = Config.MinVelocityForAirJump;
            Config.MinVelocityForAirJump = value;
            SaveConfig();
            command.ReplyToCommand($"[DoubleJump] MinVelocityForAirJump изменён с {old.ToString(CultureInfo.InvariantCulture)} на {value.ToString(CultureInfo.InvariantCulture)}.");
            Log(LogLevel.Information, $"MinVelocityForAirJump изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand($"[DoubleJump] Неверное значение. Введите число <= 0 (можно с точкой).");
        }
    }

    private void OnSetAirJumpMultiplierCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[DoubleJump] Текущее значение AirJumpBoostMultiplier: {Config.AirJumpBoostMultiplier.ToString(CultureInfo.InvariantCulture)} (по умолч. 1.0). Использование: css_doublejump_setairjumpmultiplier <число >=0>");
            return;
        }

        string arg = command.GetArg(1).Replace(',', '.');
        if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value >= 0)
        {
            float old = Config.AirJumpBoostMultiplier;
            Config.AirJumpBoostMultiplier = value;
            SaveConfig();
            command.ReplyToCommand($"[DoubleJump] AirJumpBoostMultiplier изменён с {old.ToString(CultureInfo.InvariantCulture)} на {value.ToString(CultureInfo.InvariantCulture)}.");
            Log(LogLevel.Information, $"AirJumpBoostMultiplier изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand($"[DoubleJump] Неверное значение. Введите неотрицательное число (можно с точкой).");
        }
    }

    private void OnSetLogLevelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[DoubleJump] Текущее значение LogLevel: {Config.LogLevel} (0-Trace,1-Debug,2-Info,3-Warning,4-Error,5-Critical). Использование: css_doublejump_setloglevel <0-5>");
            return;
        }

        string arg = command.GetArg(1);
        if (int.TryParse(arg, out int value) && value >= 0 && value <= 5)
        {
            int old = Config.LogLevel;
            Config.LogLevel = value;
            SaveConfig();
            command.ReplyToCommand($"[DoubleJump] LogLevel изменён с {old} на {value}.");
            Log(LogLevel.Information, $"LogLevel изменён с {old} на {value}.");
        }
        else
        {
            command.ReplyToCommand($"[DoubleJump] Неверное значение. Используйте целое число от 0 до 5.");
        }
    }

    private void SaveConfig()
    {
        try
        {
            string configPath = Path.Combine(Server.GameDirectory, "counterstrikesharp", "configs", "plugins", "CS2_DoubleJump", "CS2_DoubleJump.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            string json = System.Text.Json.JsonSerializer.Serialize(Config, options);
            File.WriteAllText(configPath, json);
            Log(LogLevel.Information, $"Конфигурация сохранена в {configPath}");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"Ошибка сохранения конфига: {ex.Message}");
        }
    }
}