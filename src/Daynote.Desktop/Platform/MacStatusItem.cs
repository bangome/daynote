using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Daynote.Desktop.Platform;

/// <summary>A rectangle in Cocoa screen coordinates: points, origin at the primary screen's bottom-left.</summary>
public readonly record struct CocoaRect(double X, double Y, double Width, double Height)
{
    public double Top => Y + Height;
}

/// <summary>
/// The menu bar status item, as AppKit's own <c>NSStatusItem</c> (menu bar design §01, Motion M9).
/// </summary>
/// <remarks>
/// Native rather than Avalonia's <c>TrayIcon</c>, for three things the latter cannot do on the Mac:
/// put the count beside the symbol as text the menu bar sets in its own font, say where the item is
/// so the popover can hang under it (an opening from the shortcut has no click to read a position
/// from), and take a left click and a right click differently — with a menu attached, AppKit gives
/// every click to the menu. The button is highlighted while the popover is up, which is what
/// <c>NSPopover</c> does to the item that opened it.
/// <para>
/// Everything here runs on the main thread, which is Avalonia's UI thread on the Mac.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacStatusItem : IDisposable
{
    private const string TargetClassName = "DaynoteStatusItemTarget";
    private const nint VariableLength = -1;
    private const nint ImageLeft = 2;
    private const nint LeftMouseDownMask = 1 << 1;
    private const nint RightMouseDownMask = 1 << 3;
    private const nuint RightMouseDown = 3;
    private const nuint ControlKeyMask = 1 << 18;

    private static MacStatusItem? current;

    // The class's methods are native callbacks into these; they live as long as the process does,
    // because an Objective-C class cannot be unregistered.
    private static readonly ObjC.ActionCallback ClickedCallback = OnClicked;
    private static readonly ObjC.ActionCallback ShowCallback = OnShow;
    private static readonly ObjC.ActionCallback QuitCallback = OnQuit;
    private static IntPtr targetClass;

    private readonly IntPtr item;
    private readonly IntPtr button;
    private readonly IntPtr target;
    private IntPtr menu;
    private IntPtr showItem;
    private IntPtr quitItem;
    private bool disposed;

    public MacStatusItem(byte[] templatePng, string toolTip)
    {
        ArgumentNullException.ThrowIfNull(templatePng);
        if (current is not null)
        {
            throw new InvalidOperationException("Only one status item is supported.");
        }

        current = this;
        target = ObjC.Send(ObjC.Send(EnsureTargetClass(), ObjC.Sel("alloc")), ObjC.Sel("init"));

        IntPtr statusBar = ObjC.Send(ObjC.Class("NSStatusBar"), ObjC.Sel("systemStatusBar"));
        item = ObjC.Send(ObjC.Send(statusBar, ObjC.Sel("statusItemWithLength:"), VariableLength), ObjC.Sel("retain"));
        button = ObjC.Send(item, ObjC.Sel("button"));

        SetImage(templatePng);
        ObjC.Send(button, ObjC.Sel("setImagePosition:"), ImageLeft);
        ObjC.Send(button, ObjC.Sel("setTarget:"), target);
        ObjC.Send(button, ObjC.Sel("setAction:"), ObjC.Sel("statusClicked:"));
        ObjC.Send(button, ObjC.Sel("sendActionOn:"), LeftMouseDownMask | RightMouseDownMask);
        SetToolTip(toolTip);
    }

    /// <summary>A left click on the item.</summary>
    public event EventHandler? Clicked;

    /// <summary>"Show Daynote" from the right-click menu.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>"Quit" from the right-click menu.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>The number beside the symbol; nothing at all when there is nothing left.</summary>
    public void SetCount(int count)
    {
        using var title = new ObjC.NSString(count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        ObjC.Send(button, ObjC.Sel("setTitle:"), title.Handle);
    }

    public void SetToolTip(string text)
    {
        using var tip = new ObjC.NSString(text);
        ObjC.Send(button, ObjC.Sel("setToolTip:"), tip.Handle);
    }

    /// <summary>Pressed-in while the popover it opened is on screen.</summary>
    public void SetHighlighted(bool highlighted) => ObjC.SendBool(button, ObjC.Sel("highlight:"), highlighted);

    public void SetVisible(bool visible) => ObjC.SendBool(item, ObjC.Sel("setVisible:"), visible);

    /// <summary>The two items of the right-click menu, in the current language.</summary>
    public void SetMenuTitles(string show, string quit)
    {
        if (menu == IntPtr.Zero)
        {
            menu = ObjC.Send(ObjC.Send(ObjC.Class("NSMenu"), ObjC.Sel("alloc")), ObjC.Sel("init"));
            showItem = AddMenuItem("statusShow:");
            ObjC.Send(menu, ObjC.Sel("addItem:"), ObjC.Send(ObjC.Class("NSMenuItem"), ObjC.Sel("separatorItem")));
            quitItem = AddMenuItem("statusQuit:");
        }

        using var showTitle = new ObjC.NSString(show);
        using var quitTitle = new ObjC.NSString(quit);
        ObjC.Send(showItem, ObjC.Sel("setTitle:"), showTitle.Handle);
        ObjC.Send(quitItem, ObjC.Sel("setTitle:"), quitTitle.Handle);
    }

    /// <summary>Where the item is on screen, or null while it is hidden or not yet placed.</summary>
    public CocoaRect? Frame
    {
        get
        {
            IntPtr window = ObjC.Send(button, ObjC.Sel("window"));
            if (window == IntPtr.Zero || !ObjC.SendBoolResult(item, ObjC.Sel("isVisible")))
            {
                return null;
            }

            CocoaRect frame = ObjC.SendRect(window, ObjC.Sel("frame"));
            return frame.Width > 0 ? frame : null;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        IntPtr statusBar = ObjC.Send(ObjC.Class("NSStatusBar"), ObjC.Sel("systemStatusBar"));
        ObjC.Send(statusBar, ObjC.Sel("removeStatusItem:"), item);
        ObjC.Send(item, ObjC.Sel("release"));
        if (menu != IntPtr.Zero)
        {
            ObjC.Send(menu, ObjC.Sel("release"));
        }

        ObjC.Send(target, ObjC.Sel("release"));
        current = null;
    }

    private void SetImage(byte[] png)
    {
        // NSData copies the bytes, so the unmanaged buffer only has to outlive the call.
        IntPtr bytes = Marshal.AllocHGlobal(png.Length);
        IntPtr data;
        try
        {
            Marshal.Copy(png, 0, bytes, png.Length);
            data = ObjC.Send(ObjC.Class("NSData"), ObjC.Sel("dataWithBytes:length:"), bytes, (nint)png.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(bytes);
        }

        IntPtr image = ObjC.Send(ObjC.Send(ObjC.Class("NSImage"), ObjC.Sel("alloc")), ObjC.Sel("initWithData:"), data);

        // Drawn at twice the size it is shown, so it is sharp on a Retina menu bar. A template image:
        // AppKit inks it to match the bar, light or dark, and inverts it while highlighted.
        ObjC.SendSize(image, ObjC.Sel("setSize:"), 18, 18);
        ObjC.SendBool(image, ObjC.Sel("setTemplate:"), true);
        ObjC.Send(button, ObjC.Sel("setImage:"), image);
        ObjC.Send(image, ObjC.Sel("release"));
    }

    private IntPtr AddMenuItem(string action)
    {
        using var empty = new ObjC.NSString(string.Empty);
        IntPtr menuItem = ObjC.Send(
            ObjC.Send(ObjC.Class("NSMenuItem"), ObjC.Sel("alloc")),
            ObjC.Sel("initWithTitle:action:keyEquivalent:"),
            empty.Handle,
            ObjC.Sel(action),
            empty.Handle);
        ObjC.Send(menuItem, ObjC.Sel("setTarget:"), target);
        ObjC.Send(menu, ObjC.Sel("addItem:"), menuItem);
        ObjC.Send(menuItem, ObjC.Sel("release"));
        return menuItem;
    }

    /// <summary>
    /// A right click (or a control-click) opens the menu the way AppKit's own items do: attached
    /// for the length of one click, so a left click is still the popover's.
    /// </summary>
    private void ShowMenu()
    {
        if (menu == IntPtr.Zero)
        {
            return;
        }

        ObjC.Send(item, ObjC.Sel("setMenu:"), menu);
        ObjC.Send(button, ObjC.Sel("performClick:"), IntPtr.Zero);
        ObjC.Send(item, ObjC.Sel("setMenu:"), IntPtr.Zero);
    }

    private static IntPtr EnsureTargetClass()
    {
        if (targetClass != IntPtr.Zero)
        {
            return targetClass;
        }

        targetClass = ObjC.Class(TargetClassName);
        if (targetClass != IntPtr.Zero)
        {
            return targetClass;
        }

        targetClass = ObjC.objc_allocateClassPair(ObjC.Class("NSObject"), TargetClassName, 0);
        ObjC.class_addMethod(targetClass, ObjC.Sel("statusClicked:"), Marshal.GetFunctionPointerForDelegate(ClickedCallback), "v@:@");
        ObjC.class_addMethod(targetClass, ObjC.Sel("statusShow:"), Marshal.GetFunctionPointerForDelegate(ShowCallback), "v@:@");
        ObjC.class_addMethod(targetClass, ObjC.Sel("statusQuit:"), Marshal.GetFunctionPointerForDelegate(QuitCallback), "v@:@");
        ObjC.objc_registerClassPair(targetClass);
        return targetClass;
    }

    private static void OnClicked(IntPtr self, IntPtr selector, IntPtr sender)
    {
        if (current is not { } status)
        {
            return;
        }

        IntPtr app = ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));
        IntPtr theEvent = ObjC.Send(app, ObjC.Sel("currentEvent"));
        bool secondary = theEvent != IntPtr.Zero
            && ((nuint)ObjC.Send(theEvent, ObjC.Sel("type")) == RightMouseDown
                || ((nuint)ObjC.Send(theEvent, ObjC.Sel("modifierFlags")) & ControlKeyMask) != 0);

        if (secondary)
        {
            status.ShowMenu();
        }
        else
        {
            status.Clicked?.Invoke(status, EventArgs.Empty);
        }
    }

    private static void OnShow(IntPtr self, IntPtr selector, IntPtr sender) =>
        current?.ShowRequested?.Invoke(current, EventArgs.Empty);

    private static void OnQuit(IntPtr self, IntPtr selector, IntPtr sender) =>
        current?.QuitRequested?.Invoke(current, EventArgs.Empty);
}

/// <summary>The handful of AppKit calls the status item and the popover's placement need.</summary>
[SupportedOSPlatform("macos")]
internal static class ObjC
{
    private const string Library = "/usr/lib/libobjc.A.dylib";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ActionCallback(IntPtr self, IntPtr selector, IntPtr sender);

    public static IntPtr Class(string name) => objc_getClass(name);

    public static IntPtr Sel(string name) => sel_registerName(name);

    /// <summary>The primary screen's frame: the one Cocoa's coordinates are measured from.</summary>
    public static CocoaRect PrimaryScreenFrame()
    {
        IntPtr screens = Send(Class("NSScreen"), Sel("screens"));
        return SendRect(Send(screens, Sel("objectAtIndex:"), (nint)0), Sel("frame"));
    }

    /// <summary>System Settings → Accessibility → Display → Reduce motion.</summary>
    public static bool ReduceMotion()
    {
        IntPtr workspace = Send(Class("NSWorkspace"), Sel("sharedWorkspace"));
        return SendBoolResult(workspace, Sel("accessibilityDisplayShouldReduceMotion"));
    }

    public static CocoaRect SendRect(IntPtr receiver, IntPtr selector)
    {
        // An NSRect is four doubles: returned in registers on arm64, through memory on x64.
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return objc_msgSend_rect(receiver, selector);
        }

        objc_msgSend_stret(out CocoaRect rect, receiver, selector);
        return rect;
    }

    public static void SendBool(IntPtr receiver, IntPtr selector, bool value) =>
        objc_msgSend_bool(receiver, selector, value);

    public static bool SendBoolResult(IntPtr receiver, IntPtr selector) =>
        objc_msgSend_boolResult(receiver, selector);

    public static void SendSize(IntPtr receiver, IntPtr selector, double width, double height) =>
        objc_msgSend_size(receiver, selector, new CocoaSize(width, height));

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr first, nint second);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second, IntPtr third);

    [DllImport(Library, EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library, EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library)]
    public static extern IntPtr objc_allocateClassPair(IntPtr superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint extraBytes);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(Library)]
    public static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    private static extern CocoaRect objc_msgSend_rect(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend_stret")]
    private static extern void objc_msgSend_stret(out CocoaRect result, IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_boolResult(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_size(IntPtr receiver, IntPtr selector, CocoaSize size);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CocoaSize(double Width, double Height);

    /// <summary>An owned NSString, released when disposed.</summary>
    public readonly struct NSString : IDisposable
    {
        private const nuint Utf16LittleEndian = 0x94000100;

        public NSString(string value)
        {
            IntPtr allocated = Send(Class("NSString"), Sel("alloc"));
            IntPtr chars = Marshal.StringToHGlobalUni(value);
            try
            {
                Handle = objc_msgSend_initWithBytes(
                    allocated, Sel("initWithBytes:length:encoding:"), chars, (nuint)(value.Length * 2), Utf16LittleEndian);
            }
            finally
            {
                Marshal.FreeHGlobal(chars);
            }
        }

        public IntPtr Handle { get; }

        public void Dispose() => Send(Handle, Sel("release"));

        [DllImport(Library, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_initWithBytes(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length, nuint encoding);
    }
}
