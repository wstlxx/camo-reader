// Set this to hide the console window that normally appears on Windows
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use eframe::egui;
use eframe::egui::{FontId, Label, RichText, Rgba, ScrollArea, Frame, ViewportCommand};
use global_hotkey::hotkey::{Code, HotKey, Modifiers};
use global_hotkey::{GlobalHotKeyEvent, GlobalHotKeyManager};
use configparser::ini::Ini;
use once_cell::sync::OnceCell;
use std::env;
use std::fs;
use std::sync::mpsc;
use std::time::{Duration, SystemTime};
use tray_item::{IconSource, TrayItem};
use std::sync::Mutex;
use log::{info, error};

// --- Configuration Struct ---
struct Config {
    width: f32,
    height: f32,
    x: f32,
    y: f32,
    text_alpha: u8,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            width: 800.0,
            height: 600.0,
            x: 100.0,
            y: 100.0,
            text_alpha: 150, // Default 150/255 opacity
        }
    }
}

fn load_config() -> Config {
    let mut config = Config::default();
    let mut conf = Ini::new();
    if conf.load("config.ini").is_ok() {
        config.width = conf.get("window", "width").and_then(|s| s.parse().ok()).unwrap_or(config.width);
        config.height = conf.get("window", "height").and_then(|s| s.parse().ok()).unwrap_or(config.height);
        config.x = conf.get("window", "x").and_then(|s| s.parse().ok()).unwrap_or(config.x);
        config.y = conf.get("window", "y").and_then(|s| s.parse().ok()).unwrap_or(config.y);
        config.text_alpha = conf
            .get("text", "color_shift_level")
            .and_then(|s| s.parse().ok())
            .unwrap_or(config.text_alpha);
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
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
enum UserEvent {
    ToggleVisibility,
    PageUp,
    PageDown,
    Quit,
}

static EVENT_RECEIVER: OnceCell<Mutex<mpsc::Receiver<UserEvent>>> = OnceCell::new();

struct CamoReaderApp {
    text_content: String,
    config: Config,
    scroll_offset: f32,
    is_visible: bool,
}

impl eframe::App for CamoReaderApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        if let Some(receiver) = EVENT_RECEIVER.get() {
            if let Ok(event) = receiver.lock().unwrap().try_recv() {
                info!("Received event: {:?}", event);
                match event {
                    UserEvent::ToggleVisibility => {
                        self.is_visible = !self.is_visible;
                        ctx.send_viewport_cmd(ViewportCommand::Visible(self.is_visible));
                    }
                    UserEvent::PageUp => {
                        self.scroll_offset -= self.config.height - 25.0;
                        if self.scroll_offset < 0.0 {
                            self.scroll_offset = 0.0;
                        }
                    }
                    UserEvent::PageDown => {
                        self.scroll_offset += self.config.height - 25.0;
                    }
                    UserEvent::Quit => {
                        ctx.send_viewport_cmd(ViewportCommand::Close);
                    }
                }
            }
        }

        let text_color = Rgba::from_rgba_unmultiplied(
            1.0,
            1.0,
            1.0,
            self.config.text_alpha as f32 / 255.0,
        );

        let frame_style = Frame::NONE.fill(egui::Color32::TRANSPARENT);

        egui::CentralPanel::default().frame(frame_style).show(ctx, |ui| {
            let scroll_area = ScrollArea::vertical()
                .auto_shrink([false; 2])
                .show(ui, |ui| {
                    ui.add(
                        Label::new(
                            RichText::new(&self.text_content)
                                .font(FontId::proportional(20.0))
                                .color(text_color),
                        )
                        .wrap(),
                    );
                });

            let mut state = scroll_area.state;
            state.offset.y = self.scroll_offset;
            state.store(ctx, scroll_area.id);
            self.scroll_offset = state.offset.y;
        });
    }
}

use std::fs::File;

fn main() {
    env_logger::Builder::from_default_env()
        .target(env_logger::Target::Pipe(Box::new(File::create("camo-reader.log").unwrap())))
        .init();
    info!("Starting Camo Reader");

    let (tx, rx) = mpsc::channel();
    EVENT_RECEIVER.set(Mutex::new(rx)).unwrap();

    let config = load_config();
    let text_content = find_and_load_latest_txt();

    // --- Set up Tray Icon ---
    let tx_clone = tx.clone();
    std::thread::spawn(move || {
        info!("Tray icon thread started");
        let mut tray = match TrayItem::new(
            "Camo Reader",
            IconSource::Resource("icon.ico"),
        ) {
            Ok(tray) => tray,
            Err(e) => {
                error!("Failed to create tray icon: {}", e);
                return;
            }
        };
        info!("Tray icon created");

        tray.add_label("Camo Reader").unwrap();
        tray.add_menu_item("Quit", move || {
            tx_clone.send(UserEvent::Quit).ok();
        })
        .unwrap();
        info!("Tray menu items added");
    });

    // --- Set up Global Hotkeys ---
    std::thread::spawn(move || {
        info!("Hotkey thread started");
        let manager = match GlobalHotKeyManager::new() {
            Ok(manager) => manager,
            Err(e) => {
                error!("Failed to create hotkey manager: {}", e);
                return;
            }
        };
        info!("Hotkey manager created");
        
        let toggle_visibility_key = HotKey::new(Some(Modifiers::CONTROL), Code::F1);
        let page_up_key = HotKey::new(None, Code::F3);
        let page_down_key = HotKey::new(None, Code::F4);

        if let Err(e) = manager.register(toggle_visibility_key) {
            error!("Failed to register toggle visibility hotkey: {}", e);
        }
        if let Err(e) = manager.register(page_up_key) {
            error!("Failed to register page up hotkey: {}", e);
        }
        if let Err(e) = manager.register(page_down_key) {
            error!("Failed to register page down hotkey: {}", e);
        }
        info!("Hotkeys registered");

        loop {
            if let Ok(event) = GlobalHotKeyEvent::receiver().try_recv() {
                let user_event = match event.id {
                    id if id == toggle_visibility_key.id() => Some(UserEvent::ToggleVisibility),
                    id if id == page_up_key.id() => Some(UserEvent::PageUp),
                    id if id == page_down_key.id() => Some(UserEvent::PageDown),
                    _ => None,
                };
                if let Some(event) = user_event {
                    tx.send(event).ok();
                }
            }
            std::thread::sleep(Duration::from_millis(50));
        }
    });

    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size(egui::vec2(config.width, config.height))
            .with_position(egui::pos2(config.x, config.y))
            .with_decorations(false)
            .with_transparent(true)
            .with_always_on_top(),
        ..Default::default()
    };

    info!("Starting eframe");
    if let Err(e) = eframe::run_native(
        "Camo Reader",
        options,
        Box::new(|_cc| {
            Ok(Box::new(CamoReaderApp {
                text_content,
                config,
                scroll_offset: 0.0,
                is_visible: false,
            }))
        }),
    ) {
        error!("Failed to run eframe: {}", e);
    }
}
