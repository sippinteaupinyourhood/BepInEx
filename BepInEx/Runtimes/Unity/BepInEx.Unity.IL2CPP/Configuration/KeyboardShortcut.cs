using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace BepInEx.Unity.IL2CPP.Configuration;










public struct KeyboardShortcut
{
    static KeyboardShortcut()
    {
        TomlTypeConverter.AddConverter(
                                       typeof(KeyboardShortcut),
                                       new TypeConverter
                                       {
                                           ConvertToString = (o, type) => ((KeyboardShortcut) o).Serialize(),
                                           ConvertToObject = (s, type) => Deserialize(s)
                                       });
    }

    
    
    
    public static readonly KeyboardShortcut Empty = new();

    
    private static KeyCode[] modifierBlockKeyCodes;

    private readonly KeyCode[] _allKeys;

    
    
    
    
    
    public KeyboardShortcut(KeyCode mainKey, params KeyCode[] modifiers) : this(new[] { mainKey }.Concat(modifiers)
                                                                                    .ToArray())
    {
        if (mainKey == KeyCode.None && modifiers.Any())
            throw new
                ArgumentException($"Can't set {nameof(mainKey)} to KeyCode.None if there are any {nameof(modifiers)}");
    }

    private KeyboardShortcut(KeyCode[] keys)
    {
        _allKeys = SanitizeKeys(keys);
    }

    private static KeyCode[] SanitizeKeys(params KeyCode[] keys)
    {
        if (keys.Length == 0 || keys[0] == KeyCode.None)
            return new[] { KeyCode.None };

        return new[] { keys[0] }.Concat(keys.Skip(1).Distinct().Where(x => x != keys[0]).OrderBy(x => (int) x))
                                .ToArray();
    }

    
    
    
    
    public KeyCode MainKey => _allKeys != null && _allKeys.Length > 0 ? _allKeys[0] : KeyCode.None;

    
    
    
    public IEnumerable<KeyCode> Modifiers => _allKeys?.Skip(1) ?? Enumerable.Empty<KeyCode>();

    
    
    
    public static KeyboardShortcut Deserialize(string str)
    {
        try
        {
            var parts = str.Split(new[] { ' ', '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(x => (KeyCode) Enum.Parse(typeof(KeyCode), x)).ToArray();
            return new KeyboardShortcut(parts);
        }
        catch (SystemException ex)
        {
            Logger.Log(LogLevel.Error, "Failed to read keybind from settings: " + ex.Message);
            return Empty;
        }
    }

    
    
    
    public string Serialize()
    {
        if (_allKeys == null) return string.Empty;
        return string.Join(" + ", _allKeys.Select(x => x.ToString()).ToArray());
    }

    
    
    
    public bool IsDown()
    {
        var mainKey = MainKey;
        if (mainKey == KeyCode.None) return false;

        return UnityInput.Current.GetKeyDown(mainKey) && ModifierKeyTest();
    }

    
    
    
    public bool IsPressed()
    {
        var mainKey = MainKey;
        if (mainKey == KeyCode.None) return false;

        return UnityInput.Current.GetKey(mainKey) && ModifierKeyTest();
    }

    
    
    
    public bool IsUp()
    {
        var mainKey = MainKey;
        if (mainKey == KeyCode.None) return false;

        return UnityInput.Current.GetKeyUp(mainKey) && ModifierKeyTest();
    }

    private bool ModifierKeyTest()
    {
        var allKeys = _allKeys;
        var mainKey = MainKey;

        var allModifiersPressed = allKeys.All(c => c == mainKey || UnityInput.Current.GetKey(c));
        if (!allModifiersPressed) return false;

        
        modifierBlockKeyCodes ??= UnityInput.Current.SupportedKeyCodes.Except([KeyCode.Mouse0, KeyCode.Mouse1,
            KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4, KeyCode.Mouse5, KeyCode.Mouse6, KeyCode.None
        ]).ToArray();

        bool noOtherModifiersPressed = modifierBlockKeyCodes.All(c => !UnityInput.Current.GetKey(c) || allKeys.Contains(c));
        return noOtherModifiersPressed;
    }

    
    public override string ToString()
    {
        if (MainKey == KeyCode.None) return "Not set";

        return string.Join(" + ", _allKeys.Select(c => c.ToString()).ToArray());
    }

    
    public override bool Equals(object obj) =>
        obj is KeyboardShortcut shortcut && MainKey == shortcut.MainKey &&
        Modifiers.SequenceEqual(shortcut.Modifiers);

    
    public override int GetHashCode()
    {
        if (MainKey == KeyCode.None) return 0;

        return _allKeys.Aggregate(_allKeys.Length, (current, item) => unchecked(current * 31 + (int) item));
    }
}
