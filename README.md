# camo-reader
Project Specification: Camo-Reader (Windows)
1. Project Overview
Camo-Reader is a native Windows 10/11 desktop application designed to function as an "always-on-top," semi-transparent text reader. The overlay window must be non-interactive ("click-through") and controlled exclusively by global hotkeys.

A key feature is the "adaptive text" engine, which analyzes the screen content behind the overlay to dynamically shift the text color, ensuring constant readability against any background.

2. Core Technology Stack
Platform: Windows 10 & Windows 11

Language: C#

Framework:.NET (e.g.,.NET 6 or later)

Application Model: Windows Forms (WinForms)

Note: WinForms is explicitly recommended over WPF. WPF's standard "click-through" properties (like IsHitTestVisible) are insufficient for passing clicks to applications below the window. WinForms provides a more direct and reliable low-level API hook (CreateParams) to achieve true system-wide click-passthrough.   

3. Core Features & Implementation
3.1. Main Application Window (Overlay)
The main window is a borderless form that serves as the text overlay.

Behavior:

Always-on-Top: The window must remain visible over all other applications, including fullscreen ones.

Implementation: Set the form's TopMost = true property.   

Frameless & Sized: The window must have no border, title bar, or OS "chrome."

Implementation: Set FormBorderStyle = FormBorderStyle.None.   

Implementation: The window's initial position (X, Y) and size (Width, Height) must be loaded from config.ini.   

Semi-Transparent: The entire window and its contents (text) will be semi-transparent.

Implementation: Set the form's Opacity property (e.g., 0.75). This value should be configurable, ideally in the INI file.

"Click-Through" (Input Passthrough): This is the most critical requirement. The window must ignore all mouse interactions (clicks, hovers, scrolls), passing them to the window directly beneath it.

Implementation: Override the form's CreateParams property. This injects the necessary window styles before the window is created, preventing any visible flicker.   

The ExStyle property must be modified to include the WS_EX_LAYERED (0x80000) and WS_EX_TRANSPARENT (0x20) flags.   

WS_EX_TRANSPARENT is the key flag that instructs Windows to pass all mouse messages through this window to the underlying applications.

3.2. Global Hotkey Management
The application must respond to system-wide hotkeys, even when it is not the active window.

Hotkeys:

F1: Toggle window visibility (Show / Hide).

F3: Navigate to the previous page of text.

F4: Navigate to the next page of text.

Implementation:

Use the native WinAPI RegisterHotKey function via P/Invoke. This is the standard, efficient method.

Do not use SetWindowsHookEx. This is an invasive, low-level keyboard hook that is often flagged by security software and has unnecessary performance overhead.   

The application's main form must override its WndProc (Window Procedure) to listen for and process the WM_HOTKEY message, which will be sent by the OS when a registered hotkey is pressed.

3.3. System Tray Integration
The application will run primarily from the system tray (notification area).

Icon: The application must display an icon in the system tray.

"No Extra File" Icon: To meet the requirement for a "transparent image" without a separate .ico file:

Implementation: Programmatically create a new Bitmap object (e.g., 32x32 pixels) in memory.

Get a Graphics object from this bitmap and use Graphics.Clear(Color.Transparent).

Convert this transparent Bitmap to an Icon using Icon.FromHandle(bitmap.GetHicon()).

Assign this generated icon to the NotifyIcon.Icon property.

Functionality:

A single left-click on the NotifyIcon must trigger the same "Toggle Visibility" action as the F1 hotkey.

A right-click context menu should be provided with at least two options: "Show/Hide" and "Exit."

3.4. Adaptive Text Engine ("Camo" Feature)
The application will read the text file specified in config.ini and display it. The text color must dynamically update to contrast with the screen content behind it.

1. Text Loading:

On startup, read the file_path from config.ini.

Load the entire text file into memory.

Paginate the text based on the window's Width, Height, and TextSize to determine how much text fits on one "page."

2. Screen Capture:

Implementation: Use the modern Windows.Graphics.Capture (WGC) API.   

This is mandatory because legacy methods (like GDI BitBlt) will fail to capture content from hardware-accelerated windows, such as web browsers, video players, and modern IDEs, returning only a black or white rectangle.   

3. Background Analysis:

Trigger this analysis when the page is turned (F3/F4) or when the window is moved (if drag functionality is added later).

Capture a snapshot of the screen region defined by the overlay's bounds.

Calculate the average perceived luminance (brightness) of the captured region.

Implementation: Use the standard "luma" formula: Brightness = ((R * 299) + (G * 587) + (B * 114)) / 1000. This provides a value from 0 (black) to 255 (white).   

4. Text Color Logic:

Define a brightness threshold based on the brightness_shift_ratio from the config.

Implementation: Threshold = 255 * (BrightnessShiftRatio / 100). A default of 50 (maps to ~127) is standard.

Set the text color based on the result:

if (AverageBrightness > Threshold): Use Color.Black.

else: Use Color.White.

5. Text Rendering:

The form will render the current page of text using Graphics.DrawString during its OnPaint event, using the dynamically calculated color.

Note on color_shift_ratio: This parameter is ambiguous. The primary method (luminance) is a brightness shift. The color_shift_ratio could be implemented as a more complex algorithm (like WCAG contrast ratio ), but for this spec, we will prioritize the luminance-based brightness shift. The color_shift_ratio can be reserved for a future "Method 2" that analyzes dominant color instead of just brightness.   

4. Configuration File (config.ini)
The application must read and write its settings to a config.ini file located in the same directory as the executable.

Ini, TOML


; Initial window position
WindowPosX = 100
WindowPosY = 100

; Initial window size
WindowWidth = 800
WindowHeight = 200

; Font size
TextSize = 14

; Full path to the.txt file to be read
TextFilePath = C:\Users\YourUser\Documents\book.txt

; Threshold for flipping text color (1-100).
; 50 = flip at 50% gray.
BrightnessShiftRatio = 50

; (Reserved for future use, e.g., WCAG contrast)
ColorShiftRatio = 50
5. Critical Implementation Risks & Mitigations
Risk: The Windows.Graphics.Capture (WGC) API displays a mandatory yellow security border around any captured area by default. This will ruin the "camo" overlay aesthetic.   

Mitigation: This border can be disabled programmatically, but the API to do so is only available on specific OS versions.

Implementation: After creating the GraphicsCaptureSession, you must query it for the IGraphicsCaptureSession3 interface.   

On this interface, set the IsBorderRequired = false property.

Constraint: This IsBorderRequired property is only supported on Windows 11 and Windows 10 (version 2104 or newer).   

Required Action: The application must perform an OS version check.

If (Win11 or Win10 >= 2104): Disable the border using the API.

If (Older Win10): The adaptive text feature must be disabled, or the user must be warned that a yellow border will appear. Falling back to BitBlt is an option, but you must accept it will not work on most modern applications.