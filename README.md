# CS2_DoubleJump

Плагин позволяет игрокам выполнять двойные прыжки в CS2. Поддерживается прыжок в воздухе при падении (air jump) с возможностью настройки высоты, количества прыжков и множителя. Боты также могут использовать двойной прыжок (если управление позволяет).


# https://www.youtube.com/watch?v=voXIQCIxAN4

# Требования
```
CounterStrikeSharp API версии 362 или выше
.NET 8.0 Runtime
```

# Конфигурационные параметры
```
css_doublejump_enabled <0/1>, def.=1 – Включение/выключение плагина.
css_doublejump_boost_units <число ≥0>, def.=300.0 – Высота дополнительного прыжка.
css_doublejump_max_jumps <1-5>, def.=1 – Максимальное количество дополнительных прыжков.
css_doublejump_air_jump_enabled <0/1>, def.=1 – Разрешить прыжок в воздухе при падении.
css_doublejump_min_velocity_for_air_jump <число ≤0>, def.=-50.0 – Минимальная вертикальная скорость (отрицательная) для активации air jump.
css_doublejump_air_jump_boost_multiplier <число ≥0>, def.=1.0 – Множитель высоты прыжка в воздухе.
css_doublejump_loglevel <0-5>, def.=4 – Уровень логирования (0-Trace,1-Debug,2-Info,3-Warning,4-Error,5-Critical).
```

# Консольные команды
```
css_doublejump_help – Показать справку по плагину.
css_doublejump_settings – Показать текущие настройки.
css_doublejump_test – Тестовая команда (для игрока: данные о прыжках).
css_doublejump_reload – Перезагрузить конфигурацию и сбросить временные данные.
css_doublejump_setenabled <0/1> – Установить значение css_doublejump_enabled.
css_doublejump_setairjumpenabled <0/1> – Установить значение css_doublejump_air_jump_enabled.
css_doublejump_setboost <число> – Установить css_doublejump_boost_units (неотрицательное).
css_doublejump_setmaxjumps <1-5> – Установить css_doublejump_max_jumps.
css_doublejump_setminvelocity <число> – Установить css_doublejump_min_velocity_for_air_jump (≤0).
css_doublejump_setairjumpmultiplier <число> – Установить css_doublejump_air_jump_boost_multiplier (≥0).
css_doublejump_setloglevel <0-5> – Установить уровень логирования.
```

# ЭТОТ ПЛАГИН ФОРК ЭТОГО ПЛАГИНА https://forums.alliedmods.net/showthread.php?p=1819503
