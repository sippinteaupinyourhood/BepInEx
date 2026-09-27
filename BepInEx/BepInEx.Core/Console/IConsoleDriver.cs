using System;
using System.IO;

namespace BepInEx;

internal interface IConsoleDriver
{
    TextWriter StandardOut { get; }
    TextWriter ConsoleOut { get; }

    bool ConsoleActive { get; }
    bool ConsoleIsExternal { get; }

    void PreventClose();

    void Initialize(bool alreadyActive, bool useManagedEncoder);

    
    
    void CreateConsole(uint codepage);
    void DetachConsole();

    void SetConsoleColor(ConsoleColor color);

    void SetConsoleTitle(string title);
}
