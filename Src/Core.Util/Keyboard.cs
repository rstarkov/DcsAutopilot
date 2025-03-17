using System.Windows.Input;

namespace DcsAutopilot;

public class KeyEventArgs
{
    public bool Down;
    public Key Key;
    public ModifierKeys Modifiers;
}
