using Avalonia.Controls;
using Avalonia.Input.TextInput;
public class TestIme {
    public void Check(TextBox tb) {
        ITextInputMethodClient client = (ITextInputMethodClient)tb;
    }
}
