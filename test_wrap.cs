using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.TextInput;
public class ClientWrapper : ITextInputMethodClient {
    private readonly ITextInputMethodClient _inner;
    public ClientWrapper(ITextInputMethodClient inner) { _inner = inner; }
    public Rect CursorRectangle => new Rect(_inner.CursorRectangle.X, _inner.CursorRectangle.Y, _inner.CursorRectangle.Width, 15.5);
    public event EventHandler? CursorRectangleChanged { add => _inner.CursorRectangleChanged += value; remove => _inner.CursorRectangleChanged -= value; }
    public event EventHandler? TextViewportChanged { add => _inner.TextViewportChanged += value; remove => _inner.TextViewportChanged -= value; }
    public ITextInputMethodImpl? TextViewport => _inner.TextViewport;
    public bool SupportsPreedit => _inner.SupportsPreedit;
    public bool SupportsSurroundingText => _inner.SupportsSurroundingText;
    public string SurroundingText => _inner.SurroundingText;
    public Rect CharacterRect(int index) => _inner.CharacterRect(index);
    public void SetPreeditText(string? text) => _inner.SetPreeditText(text);
}
