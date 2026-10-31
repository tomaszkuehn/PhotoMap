#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
use serde::Serialize;
use std::{
    fs,
    io::BufReader,
    path::{Path, PathBuf},
};
use tauri::{Emitter, http};
use tauri_plugin_dialog::DialogExt;
use walkdir::WalkDir;

#[derive(Serialize, Clone)]
struct Photo {
    id: String,
    name: String,
    path: String,
    lat: f64,
    lon: f64,
    date: Option<String>,
}

#[derive(Serialize, Clone)]
struct ScanProgress {
    scanned: u32,
    mapped: u32,
}

#[derive(Serialize)]
struct ScanResult {
    scanned: u32,
    mapped: u32,
    photos: Vec<Photo>,
}

fn fnv1a(bytes: &[u8]) -> u64 {
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    for b in bytes {
        h ^= *b as u64;
        h = h.wrapping_mul(0x100_0000_01b3);
    }
    h
}

fn decode_path(seg: &str) -> Option<String> {
    URL_SAFE_NO_PAD
        .decode(seg)
        .ok()
        .and_then(|v| String::from_utf8(v).ok())
}

fn dms_to_deg(v: &exif::Value) -> Option<f64> {
    match v {
        exif::Value::Rational(rs) => {
            if rs.is_empty() {
                return None;
            }
            let deg = rs[0].to_f64();
            let min = rs.get(1).map(|r| r.to_f64()).unwrap_or(0.0);
            let sec = rs.get(2).map(|r| r.to_f64()).unwrap_or(0.0);
            Some(deg + min / 60.0 + sec / 3600.0)
        }
        _ => None,
    }
}

fn ascii_str(v: &exif::Value) -> Option<String> {
    match v {
        exif::Value::Ascii(items) => items
            .first()
            .and_then(|b| String::from_utf8(b.clone()).ok())
            .map(|s| s.trim_end_matches('\0').trim().to_string()),
        _ => None,
    }
}

struct ExifInfo {
    lat: f64,
    lon: f64,
    date: Option<String>,
}

fn read_exif(path: &Path) -> Option<ExifInfo> {
    let file = fs::File::open(path).ok()?;
    let mut reader = BufReader::new(file);
    let exif = exif::Reader::new().read_from_container(&mut reader).ok()?;
    let fld = |tag: exif::Tag| exif.get_field(tag, exif::In::PRIMARY);

    let lat = dms_to_deg(&fld(exif::Tag::GPSLatitude)?.value)?;
    let lon = dms_to_deg(&fld(exif::Tag::GPSLongitude)?.value)?;
    if lat == 0.0 && lon == 0.0 {
        return None; // (0,0) is almost always a missing fix
    }
    let mut lat = lat;
    let mut lon = lon;
    if let Some(s) = fld(exif::Tag::GPSLatitudeRef).and_then(|f| ascii_str(&f.value)) {
        if s.eq_ignore_ascii_case("S") {
            lat = -lat;
        }
    }
    if let Some(s) = fld(exif::Tag::GPSLongitudeRef).and_then(|f| ascii_str(&f.value)) {
        if s.eq_ignore_ascii_case("W") {
            lon = -lon;
        }
    }
    let date = fld(exif::Tag::DateTimeOriginal).and_then(|f| ascii_str(&f.value));
    Some(ExifInfo { lat, lon, date })
}

fn read_orientation_of(exif: &exif::Exif) -> u16 {
    exif.get_field(exif::Tag::Orientation, exif::In::PRIMARY)
        .and_then(|f| f.value.get_uint(0))
        .unwrap_or(1) as u16
}

fn read_orientation(src: &Path) -> u16 {
    let file = match fs::File::open(src) {
        Ok(f) => f,
        Err(_) => return 1,
    };
    let mut r = BufReader::new(file);
    match exif::Reader::new().read_from_container(&mut r) {
        Ok(e) => read_orientation_of(&e),
        Err(_) => 1,
    }
}

fn orient_image(img: image::DynamicImage, o: u16) -> image::DynamicImage {
    match o {
        2 => img.fliph(),
        3 => img.rotate180(),
        4 => img.flipv(),
        5 => img.rotate90().fliph(),
        6 => img.rotate90(),
        7 => img.rotate90().flipv(),
        8 => img.rotate270(),
        _ => img,
    }
}

fn thumb_cache_dir() -> PathBuf {
    dirs::cache_dir()
        .unwrap_or_else(std::env::temp_dir)
        .join("PhotoMap")
        .join("thumbs")
}

fn make_thumbnail(src: &Path, cache_dir: &Path) -> Option<Vec<u8>> {
    let cache_path = cache_dir.join(format!("t{:016x}.jpg", fnv1a(src.to_string_lossy().as_bytes())));
    if let Ok(bytes) = fs::read(&cache_path) {
        return Some(bytes);
    }
    let _ = fs::create_dir_all(cache_dir);
    let img = image::open(src).ok()?;
    let img = orient_image(img, read_orientation(src));
    let thumb = img.thumbnail(340, 340).into_rgb8();
    let mut out = std::io::Cursor::new(Vec::new());
    let enc = image::codecs::jpeg::JpegEncoder::new_with_quality(&mut out, 82);
    thumb.write_with_encoder(enc).ok()?;
    let bytes = out.into_inner();
    let _ = fs::write(&cache_path, &bytes);
    Some(bytes)
}

fn content_type(ext: Option<&str>) -> &'static str {
    match ext.map(|e| e.to_ascii_lowercase()).as_deref() {
        Some("jpg") | Some("jpeg") => "image/jpeg",
        Some("png") => "image/png",
        Some("gif") => "image/gif",
        Some("webp") => "image/webp",
        _ => "application/octet-stream",
    }
}

fn handle_photo_request(request: http::Request<Vec<u8>>) -> http::Response<Vec<u8>> {
    let not_found = || {
        http::Response::builder()
            .status(404)
            .header("access-control-allow-origin", "*")
            .body(Vec::new())
            .unwrap()
    };
    let ok = |ct: &str, body: Vec<u8>, immutable: bool| {
        http::Response::builder()
            .status(200)
            .header("content-type", ct)
            .header("access-control-allow-origin", "*")
            .header(
                "cache-control",
                if immutable { "max-age=31536000, immutable" } else { "no-cache" },
            )
            .body(body)
            .unwrap()
    };

    let path = request.uri().path().trim_start_matches('/');
    let (kind, seg) = match path.split_once('/') {
        Some(k) => k,
        None => return not_found(),
    };
    let file_path = match decode_path(seg) {
        Some(p) => p,
        None => return not_found(),
    };
    let src = PathBuf::from(&file_path);
    match kind {
        "t" => match make_thumbnail(&src, &thumb_cache_dir()) {
            Some(b) => ok("image/jpeg", b, true),
            None => not_found(),
        },
        "f" => match fs::read(&src) {
            Ok(b) => ok(content_type(src.extension().and_then(|e| e.to_str())), b, false),
            Err(_) => not_found(),
        },
        _ => not_found(),
    }
}

fn is_photo_file(path: &Path) -> bool {
    path.extension()
        .and_then(|e| e.to_str())
        .map(|e| {
            let e = e.to_ascii_lowercase();
            e == "jpg" || e == "jpeg"
        })
        .unwrap_or(false)
}

fn scan_dir_blocking(path: String, app: &tauri::AppHandle) -> Result<ScanResult, String> {
    if !Path::new(&path).is_dir() {
        return Err("Folder does not exist".into());
    }
    let mut photos: Vec<Photo> = Vec::new();
    let mut scanned: u32 = 0;
    let mut mapped: u32 = 0;

    for entry in WalkDir::new(&path).follow_links(false).into_iter().flatten() {
        if !entry.file_type().is_file() || !is_photo_file(entry.path()) {
            continue;
        }
        scanned += 1;
        if let Some(info) = read_exif(entry.path()) {
            let p = entry.path().to_path_buf();
            photos.push(Photo {
                id: format!("{:016x}", fnv1a(p.to_string_lossy().as_bytes())),
                name: entry.file_name().to_string_lossy().into_owned(),
                path: p.to_string_lossy().into_owned(),
                lat: info.lat,
                lon: info.lon,
                date: info.date,
            });
            mapped += 1;
        }
        if scanned % 250 == 0 {
            let _ = app.emit("scan-progress", ScanProgress { scanned, mapped });
        }
    }
    let _ = app.emit("scan-progress", ScanProgress { scanned, mapped });
    Ok(ScanResult { scanned, mapped, photos })
}

#[tauri::command]
async fn pick_folder(app: tauri::AppHandle) -> Result<Option<String>, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let picked = app.dialog().file().blocking_pick_folder();
        Ok(match picked {
            Some(fp) => Some(fp.into_path().unwrap_or_default().to_string_lossy().into_owned()),
            None => None,
        })
    })
    .await
    .map_err(|e| e.to_string())?
}

#[tauri::command]
async fn scan_folder(app: tauri::AppHandle, path: String) -> Result<ScanResult, String> {
    tauri::async_runtime::spawn_blocking(move || scan_dir_blocking(path, &app))
        .await
        .map_err(|e| e.to_string())?
}

fn main() {
    let _ = fs::create_dir_all(thumb_cache_dir());
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![pick_folder, scan_folder])
        .register_asynchronous_uri_scheme_protocol("photo", |_ctx, request, responder| {
            tauri::async_runtime::spawn(async move {
                responder.respond(handle_photo_request(request));
            });
        })
        .run(tauri::generate_context!())
        .expect("error while running PhotoMap");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fnv1a_empty_is_offset_basis() {
        assert_eq!(fnv1a(b""), 0xcbf2_9ce4_8422_2325);
    }

    #[test]
    fn fnv1a_known_vector() {
        // FNV-1a 64 reference value for "a"
        assert_eq!(fnv1a(b"a"), 0xaf63_dc4c_8601_ec8c);
    }

    #[test]
    fn decodes_urlsafe_b64() {
        assert_eq!(decode_path("aGVsbG8").unwrap(), "hello");
        assert!(decode_path("!!!").is_none());
    }

    #[test]
    fn dms_conversion() {
        let v = exif::Value::Rational(vec![
            exif::Rational { num: 41, denom: 1 },
            exif::Rational { num: 53, denom: 1 },
            exif::Rational { num: 23, denom: 1 },
        ]);
        let deg = dms_to_deg(&v).unwrap();
        assert!((deg - 41.889_722).abs() < 0.0005);
        assert!(dms_to_deg(&exif::Value::Byte(vec![])).is_none());
    }
}