using System;
using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Input.TextInput;
public class MyTextPresenter : TextPresenter, ITextInputMethodClient {
    Rect ITextInputMethodClient.CursorRectangle {
        get {
            var rect = ((ITextInputMethodClient)base).CursorRectangle;
            return new Rect(rect.X, rect.Y, rect.Width, 15.5);
        }
    }
}
