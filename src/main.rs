// Set this to hide the console window that normally appears on Windows
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use egui::{FontId, Label, RichText, Rgba, ScrollArea, Sense};
use egui_winit::State;
use egui_glow::EguiGlow;
use global_hotkey::hotkey::{Code, HotKey, Modifiers};
use global_hotkey::{GlobalHotKeyEvent, GlobalHotKeyManager};
use glow::HasContext;
use ini::Ini;
use once_cell::sync::OnceCell;
use std::collections::HashMap;
use std::env;
use std::fs;
use std::rc::Rc;
use std::sync::mpsc::{self, Receiver};
use std::time::{Duration, SystemTime};
use tray_item::{IconSource, TrayItem};
use winit::dpi::{LogicalPosition, LogicalSize, PhysicalPosition, PhysicalSize};
use winit::event::{Event, WindowEvent};
use winit::event_loop::{ControlFlow, EventLoop, EventLoopBuilder, EventLoopProxy};
use winit::platform::windows::WindowBuilderExtWindows;
use winit::window::{Window, WindowBuilder, WindowId, WindowLevel};

// --- Configuration Struct ---
struct Config {
    width: u32,
    height: u32,
    x: f64,
    y: f64,
    text_alpha: u8,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            width: 800,
            height: 600,
            x: 100.0,
            y: 100.0,
            text_alpha: 150, // Default 150/255 opacity
        }
    }
}

fn load_config() -> Config {
    let mut config = Config::default();
    if let Ok(conf) = Ini::load_from_file("config.ini") {
        if let Some(section) = conf.section(Some("window")) {
            config.width = section.get("width").and_then(|s| s.parse().ok()).unwrap_or(config.width);
            config.height = section.get("height").and_then(|s| s.parse().ok()).unwrap_or(config.height);
            config.x = section.get("x").and_then(|s| s.parse().ok()).unwrap_or(config.x);
            config.y = section.get("y").and_then(|s| s.parse().ok()).unwrap_or(config.y);
        }
        if let Some(section) = conf.section(Some("text")) {
            config.text_alpha = section
                .get("color_shift_level")
                .and_then(|s| s.parse().ok())
                .unwrap_or(config.text_alpha);
        }
    }
    config
}

// --- Text Loading ---
fn find_and_load_latest_txt() -> String {
    let current_dir = match env::current_dir() {
        Ok(dir) => dir,
        Err(e) => return format!("Error getting current directory: {}", e),
    };

    let mut latest_file = None;
    let mut latest_time = SystemTime::UNIX_EPOCH;

    if let Ok(entries) = fs::read_dir(current_dir) {
        for entry in entries.flatten() {
            if let Ok(path) = entry.path().into_os_string().into_string() {
                if path.ends_with(".txt") {
                    if let Ok(metadata) = entry.metadata() {
                        if let Ok(modified) = metadata.modified() {
                            if modified > latest_time {
                                latest_time = modified;
                                latest_file = Some(entry.path());
                            }
                        }
                    }
                }
            }
        }
    }

    match latest_file {
        Some(path) => fs::read_to_string(path).unwrap_or_else(|e| format!("Error reading file: {}", e)),
        None => "No .txt files found in this directory.\nCreate one and restart.".to_string(),
    }
}

// --- Global Event Loop Management ---
// Custom events to send from hotkeys/tray to the main window loop
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
enum UserEvent {
    ToggleVisibility,
    PageUp,
    PageDown,
    Quit,
}

// We use OnceCell to get a static EventLoopProxy we can send events from
static EVENT_LOOP_PROXY: OnceCell<EventLoopProxy<UserEvent>> = OnceCell::new();

// --- Main Application ---
fn main() {
    let config = load_config();
    let text_content = find_and_load_latest_txt();

    // 1. Set up Event Loop and Window
    let event_loop = EventLoopBuilder::<UserEvent>::with_user_event().build();
    let event_loop_proxy = event_loop.create_proxy();
    EVENT_LOOP_PROXY.set(event_loop_proxy).unwrap();

    let window_builder = WindowBuilder::new()
        .with_title("Camo Reader") // Updated name
        .with_decorations(false) // No title bar, borders, etc.
        .with_transparent(true)  // Enable transparency
        .with_window_level(WindowLevel::AlwaysOnTop)
        .with_skip_taskbar(true) // Don't show in taskbar (since we have a tray)
        .with_position(LogicalPosition::new(config.x, config.y))
        .with_inner_size(LogicalSize::new(config.width, config.height));

    // This makes the window "click-through-able"
    let window_builder = window_builder.with_undraggable_on_click(true);

    let (window, glow_context) =
        glutin_winit::DisplayBuilder::new()
            .with_window_builder(Some(window_builder))
            .build(&event_loop, |mut configs| {
                // Find a config with transparency
                configs.find(|c| c.supports_transparency().unwrap_or(false)).unwrap()
            })
            .expect("Failed to create window");
    
    let window = window.expect("Could not create window");
    let glow_context = unsafe {
        glow_context
            .make_current(&window)
            .expect("Failed to make GL context current")
    };
    let glow =
        unsafe { glow::Context::from_loader_function_arr(|s| glow_context.get_proc_address(s)) };

    // 2. Set up Tray Icon
    // We run this in a separate thread because tray-item blocks.
    std::thread::spawn(|| {
        let mut tray = TrayItem::new(
            "Camo Reader", // Updated name
            IconSource::Resource(""), // Use default icon
        )
        .expect("Failed to create tray icon");

        tray.add_label("Camo Reader").unwrap(); // Updated name
        tray.add_menu_item("Quit", || {
            if let Some(proxy) = EVENT_LOOP_PROXY.get() {
                proxy.send_event(UserEvent::Quit).ok();
            }
        })
        .unwrap();

        // This blocks the thread, which is fine
        tray_item::main_loop();
    });

    // 3. Set up Global Hotkeys
    // We also run this in a separate thread.
    let (hotkey_tx, hotkey_rx) = mpsc::channel();
    std::thread::spawn(move || {
        let manager = GlobalHotKeyManager::new().expect("Failed to create hotkey manager");
        
        // Define hotkeys
        let hide_key = HotKey::new(None, Code::F1);
        let show_key = HotKey::new(Some(Modifiers::CONTROL), Code::F1);
        let page_up_key = HotKey::new(None, Code::F3);
        let page_down_key = HotKey::new(None, Code::F4);

        // Register hotkeys
        manager.register(hide_key).unwrap();
        manager.register(show_key).unwrap();
        manager.register(page_up_key).unwrap();
        manager.register(page_down_key).unwrap();

        // Listen for events
        loop {
            if let Ok(event) = GlobalHotKeyEvent::receiver().try_recv() {
                let user_event = match event.id {
                    id if id == hide_key.id() => Some(UserEvent::ToggleVisibility),
                    id if id == show_key.id() => Some(UserEvent::ToggleVisibility),
                    id if id == page_up_key.id() => Some(UserEvent::PageUp),
                    id if id == page_down_key.id() => Some(UserEvent::PageDown),
                    _ => None,
                };
                if let Some(event) = user_event {
                    if let Some(proxy) = EVENT_LOOP_PROXY.get() {
                         proxy.send_event(event).ok();
                    }
                }
            }
            std::thread::sleep(Duration::from_millis(50));
        }
    });

    // 4. Set up Egui
    let mut egui_glow = EguiGlow::new(&event_loop, Rc::new(glow));
    let mut egui_state = State::new(egui_glow.max_texture_side() as usize, &window);

    // --- Application State ---
    let mut is_visible = true;
    let mut scroll_offset = 0.0;
    let text_style = FontId::proportional(20.0); // Font size for text
    let line_height = 25.0; // Estimated line height for paging
    
    // --- Main Event Loop ---
    event_loop.run(move |event, _, control_flow| {
        *control_flow = ControlFlow::Wait;

        match event {
            // --- Handle Custom Events from Hotkeys/Tray ---
            Event::UserEvent(user_event) => match user_event {
                UserEvent::ToggleVisibility => {
                    is_visible = !is_visible;
                    window.set_visible(is_visible);
                    if is_visible {
                        // When showing, make it "non-click-through" so user can scroll
                        window.set_undraggable_on_click(false);
                    } else {
                        // When hiding, make it click-through
                        window.set_undraggable_on_click(true);
                    }
                }
                UserEvent::PageUp => {
                    scroll_offset -= config.height as f32 - line_height; // Page by window height
                    if scroll_offset < 0.0 { scroll_offset = 0.0; }
                    window.request_redraw();
                }
                UserEvent::PageDown => {
                     scroll_offset += config.height as f32 - line_height;
                     window.request_redraw();
                }
                UserEvent::Quit => {
                    *control_flow = ControlFlow::Exit;
                }
            },

            // --- Handle Window Events ---
            Event::WindowEvent { event, .. } => {
                let event_response = egui_state.on_window_event(&window, &event);
                if event_response.repaint {
                    window.request_redraw();
                }
                if event_response.consumed {
                    return;
                }

                match event {
                    WindowEvent::CloseRequested => {
                        *control_flow = ControlFlow::Exit;
                    }
                    WindowEvent::Resized(physical_size) => {
                         glow_context.resize(physical_size);
                         window.request_redraw();
                    }
                    // This logic makes the window "click-through" when the mouse leaves it
                    WindowEvent::MouseInput { .. } => {
                        // When user clicks, make it interactable
                        window.set_undraggable_on_click(false);
                    }
                    WindowEvent::CursorLeft { .. } => {
                        // When mouse leaves, make it click-through again
                        window.set_undraggable_on_click(true);
                    }
                    _ => (),
                }
            }

            // --- Handle Redrawing ---
            Event::RedrawRequested(_) => {
                let raw_input = egui_state.take_egui_input(&window);
                let mut egui_output = egui_glow.run(&window, raw_input, |ctx| {
                    
                    // --- This is the "Simplified" Rendering (Semi-Transparent Text) ---
                    
                    // 1. Define a text color with the alpha from config.ini
                    let text_color = Rgba::from_rgba_unmultiplied(
                        1.0, 1.0, 1.0, // White text
                        config.text_alpha as f32 / 255.0, // Alpha level
                    );
                    
                    // 2. Make the main panel completely transparent
                    let frame = egui::Frame::none().fill(egui::Color32::TRANSPARENT);
                    egui::CentralPanel::default().frame(frame).show(ctx, |ui| {
                        
                        // --- This is where the "Pixel-Shift" logic would go ---
                        // 1. You would *not* use egui::Label here.
                        // 2. You would capture the screen behind the window.
                        // 3. Get the text layout (e.g., from cosmic-text).
                        // 4. For each pixel in the text layout:
                        //    a. Get the background color from the screen capture.
                        //    b. Apply your "color_shift_level" logic to it.
                        //    c. Draw that single pixel to a pixel buffer.
                        // 5. Blit the final pixel buffer to the screen.
                        // This is *extremely* complex.
                        // --- End Pixel-Shift Logic ---
                        
                        // 3. Add a ScrollArea for paging
                        let mut scroll_area = ScrollArea::vertical()
                            .auto_shrink([false; 2])
                            .show(ui, |ui| {
                                ui.add(
                                    Label::new(
                                        RichText::new(&text_content)
                                            .font(text_style.clone())
                                            .color(text_color),
                                    )
                                    .wrap(true), // Wrap text at window edge
                                );
                            });
                        
                        // 4. Manually control scrolling for F3/F4
                        let mut state = scroll_area.state.lock();
                        state.offset.y = scroll_offset;
                        scroll_area.state.store(ctx, scroll_area.id, state);
                        scroll_offset = scroll_area.state.lock().offset.y; // Save new offset if user scrolled
                    });
                });

                egui_state.handle_platform_output(&window, egui_output.platform_output);
                
                // Clear the screen with *full transparency*
                unsafe {
                    glow.clear_color(0.0, 0.0, 0.0, 0.0);
                    glow.clear(glow::COLOR_BUFFER_BIT);
                }

                // Draw egui content
                egui_glow.paint(&window, &glow_context, egui_output.textures_delta, egui_output.shapes);
                
                // Swap buffers
                glow_context.swap_buffers().unwrap();
            }
            _ => (),
        }
    });
}