// Set this to hide the console window that normally appears on Windows
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use egui::{FontId, Label, RichText, Rgba, ScrollArea};
use egui_winit::State;
use egui_glow::EguiGlow;
use global_hotkey::hotkey::{Code, HotKey, Modifiers};
use global_hotkey::{GlobalHotKeyEvent, GlobalHotKeyManager};
use glow::HasContext;
use ini::Ini;
use once_cell::sync::OnceCell;
use std::env;
use std::fs;
// FIX: Removed unused 'Rc'
use std::sync::mpsc::{self};
use std::sync::Arc;
use std::time::{Duration, SystemTime};
use tray_item::{IconSource, TrayItem};
use winit::dpi::{LogicalPosition, LogicalSize};
use winit::event::{Event, WindowEvent};
use winit::event_loop::{ControlFlow, EventLoopBuilder, EventLoopProxy};
use winit::platform::windows::WindowBuilderExtWindows;
use winit::window::{WindowBuilder, WindowLevel};

// --- FIX: Add new imports for glutin/GL setup ---
// FIX: Removed 'PossiblyCurrentContext' as it was unused
use glutin::context::{ContextAttributesBuilder};
// FIX: Removed 'NotCurrentGlContextSurfaceAccessor' as it's not in this glutin version
use glutin::display::GetGlDisplay;
use glutin::prelude::{GlDisplay};
// FIX: Removed unused 'Surface'
use glutin::surface::{GlSurface, SurfaceAttributesBuilder, WindowSurface};
// FIX: Removed unused 'HasDisplayHandle'
use raw_window_handle::{HasWindowHandle};

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
        // FIX: Corrected typo 'to-string()' to 'to_string()'
        None => "No .txt files found in this directory.\nCreate one and restart.".to_string(),
    }
}

// --- Global Event Loop Management ---
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
enum UserEvent {
    ToggleVisibility,
    PageUp,
    PageDown,
    Quit,
}

static EVENT_LOOP_PROXY: OnceCell<EventLoopProxy<UserEvent>> = OnceCell::new();

// --- Main Application ---
fn main() {
    let config = load_config();
    let text_content = find_and_load_latest_txt();

    // 1. Set up Event Loop and Window
    // FIX: Add .expect() to unwrap the Result from build()
    let event_loop = EventLoopBuilder::<UserEvent>::with_user_event()
        .build()
        .expect("Failed to create event loop");
    let event_loop_proxy = event_loop.create_proxy();
    EVENT_LOOP_PROXY.set(event_loop_proxy).unwrap();

    let window_builder = WindowBuilder::new()
        .with_title("Camo Reader")
        .with_decorations(false)
        .with_transparent(true)
        .with_window_level(WindowLevel::AlwaysOnTop)
        .with_skip_taskbar(true)
        .with_position(LogicalPosition::new(config.x, config.y))
        .with_inner_size(LogicalSize::new(config.width, config.height));

    // FIX: Renamed 'with_undraggable' back to 'with_undraggable_on_click'
    let window_builder = window_builder.with_undraggable_on_click(true);

    // --- FIX: This is the new, correct GL/Window setup ---
    // 1. Build window and GL config
    let (window, gl_config) = glutin_winit::DisplayBuilder::new()
        .with_window_builder(Some(window_builder))
        .build(&event_loop, |mut configs| {
            // Find a config with transparency
            configs.find(|c| c.supports_transparency().unwrap_or(false)).unwrap()
        })
        .expect("Failed to create window");
    
    let window = window.expect("Could not create window");
    let raw_window_handle = window.window_handle().unwrap().as_raw();
    let gl_display = gl_config.display();

    // 2. Create GL context
    let context_attributes = ContextAttributesBuilder::new().build(Some(raw_window_handle));
    let gl_context = unsafe {
        gl_display.create_context(&gl_config, &context_attributes)
    }.expect("Failed to create GL context");

    // 3. Create GL surface
    let size = window.inner_size();
    let surface_attributes = SurfaceAttributesBuilder::<WindowSurface>::new()
        .build(raw_window_handle, size.width.try_into().unwrap(), size.height.try_into().unwrap());
    let gl_surface = unsafe {
        gl_display.create_window_surface(&gl_config, &surface_attributes)
    }.expect("Failed to create GL surface");

    // 4. Make context current
    let gl_context = gl_context.make_current(&gl_surface).expect("Failed to make context current");

    // 5. Load Glow
    let glow = unsafe {
        // FIX: Use 'from_loader_function' (not ..._arr) and load from 'gl_display'
        glow::Context::from_loader_function(|s| gl_display.get_proc_address(s))
    };
    // --- End new GL setup ---


    // 2. Set up Tray Icon
    std::thread::spawn(|| {
        let mut tray = TrayItem::new(
            "Camo Reader",
            IconSource::Resource(""),
        )
        .expect("Failed to create tray icon");

        tray.add_label("Camo Reader").unwrap();
        tray.add_menu_item("Quit", || {
            if let Some(proxy) = EVENT_LOOP_PROXY.get() {
                proxy.send_event(UserEvent::Quit).ok();
            }
        })
        .unwrap();
        
        // FIX: Removed 'tray_item::main_loop();' as it's no longer needed in this version.
    });

    // 3. Set up Global Hotkeys
    std::thread::spawn(move || {
        let manager = GlobalHotKeyManager::new().expect("Failed to create hotkey manager");
        
        let hide_key = HotKey::new(None, Code::F1);
        let show_key = HotKey::new(Some(Modifiers::CONTROL), Code::F1);
        let page_up_key = HotKey::new(None, Code::F3);
        let page_down_key = HotKey::new(None, Code::F4);

        manager.register(hide_key).unwrap();
        manager.register(show_key).unwrap();
        manager.register(page_up_key).unwrap();
        manager.register(page_down_key).unwrap();

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
    // FIX: Use Arc<glow::Context> instead of Rc
    let mut egui_glow = EguiGlow::new(&event_loop, Arc::new(glow));
    // FIX: Provide all 5 arguments to State::new
    let mut egui_state = State::new(
        egui_glow.max_texture_side() as usize,
        &window,
        &window, // The window implements HasDisplayHandle
        None,    // Use default pixels_per_point
        None,    // Use default max_retained_back_buffers
    );

    // --- Application State ---
    let mut is_visible = true;
    let mut scroll_offset = 0.0;
    let text_style = FontId::proportional(20.0);
    let line_height = 25.0;
    
    // --- Main Event Loop ---
    // FIX: 'event_loop' is now unwrapped, so .run() is valid.
    event_loop.run(move |event, _, control_flow| {
        *control_flow = ControlFlow::Wait;

        match event {
            Event::UserEvent(user_event) => match user_event {
                UserEvent::ToggleVisibility => {
                    is_visible = !is_visible;
                    window.set_visible(is_visible);
                    if is_visible {
                        // FIX: Renamed 'set_undraggable' to 'set_undraggable_on_click'
                        window.set_undraggable_on_click(false);
                    } else {
                        // FIX: Renamed 'set_undraggable' to 'set_undraggable_on_click'
                        window.set_undraggable_on_click(true);
                    }
                }
                UserEvent::PageUp => {
                    scroll_offset -= config.height as f32 - line_height;
                    if scroll_offset < 0.0 { scroll_offset = 0.0; }
                    window.request_redraw();
                }
                UserEvent::PageDown => {
                     scroll_offset += config.height as f32 - line_height;
                     window.request_redraw();
                }
                UserEvent::Quit => {
                    // FIX: Use 'ExitWithCode' instead of 'Exit'
                    *control_flow = ControlFlow::ExitWithCode(0);
                }
            },

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
                        // FIX: Use 'ExitWithCode' instead of 'Exit'
                        *control_flow = ControlFlow::ExitWithCode(0);
                    }
                    WindowEvent::Resized(physical_size) => {
                         // FIX: Resize the 'gl_surface', not the 'glow_context'
                         gl_surface.resize(
                            &gl_context,
                            physical_size.width.try_into().unwrap(),
                            physical_size.height.try_into().unwrap()
                         );
                         window.request_redraw();
                    }
                    WindowEvent::MouseInput { .. } => {
                        // FIX: Renamed 'set_undraggable' to 'set_undraggable_on_click'
                        window.set_undraggable_on_click(false);
                    }
                    WindowEvent::CursorLeft { .. } => {
                        // FIX: Renamed 'set_undraggable' to 'set_undraggable_on_click'
                        window.set_undraggable_on_click(true);
                    }
                    
                    // FIX: 'RedrawRequested' is a WindowEvent, so it must be handled here.
                    WindowEvent::RedrawRequested => {
                        let raw_input = egui_state.take_egui_input(&window);
                        let mut egui_output = egui_glow.run(&window, raw_input, |ctx| {
                            
                            let text_color = Rgba::from_rgba_unmultiplied(
                                1.0, 1.0, 1.0,
                                config.text_alpha as f32 / 255.0,
                            );
                            
                            let frame = egui::Frame::none().fill(egui::Color32::TRANSPARENT);
                            egui::CentralPanel::default().frame(frame).show(ctx, |ui| {
                                
                                let mut scroll_area = ScrollArea::vertical()
                                    .auto_shrink([false; 2])
                                    .show(ui, |ui| {
                                        ui.add(
                                            Label::new(
                                                RichText::new(&text_content)
                                                    .font(text_style.clone())
                                                    .color(text_color),
                                            )
                                            .wrap(true),
                                        );
                                    });
                                
                                // FIX: Removed '.lock()' from state
                                let mut state = scroll_area.state;
                                state.offset.y = scroll_offset;
                                // FIX: '.store()' takes 2 args, not 3
                                state.store(ctx, scroll_area.id);
                                // FIX: Removed '.lock()' from state
                                scroll_offset = scroll_area.state.offset.y;
                            });
                        });

                        egui_state.handle_platform_output(&window, egui_output.platform_output);
                        
                        unsafe {
                            glow.clear_color(0.0, 0.0, 0.0, 0.0);
                            glow.clear(glow::COLOR_BUFFER_BIT);
                        }

                        egui_glow.paint(&window, &gl_context, egui_output.textures_delta, egui_output.shapes);
                        
                        // FIX: Swap buffers on 'gl_surface'
                        gl_surface.swap_buffers(&gl_context).unwrap();
                    }
                    _ => (),
                }
            }
            
            // FIX: This event is now handled inside 'WindowEvent'
            // Event::RedrawRequested(_) => { ... }
            
            _ => (),
        }
    });
}
