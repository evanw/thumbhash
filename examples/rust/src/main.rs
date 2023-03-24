use image::{ImageEncoder};
use image::codecs::png::{PngEncoder};
use thumbhash::{rgba_to_thumb_hash, thumb_hash_to_rgba};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    // Load the input image from a file
    let image = image::open("../flower.jpg").unwrap();

    // Convert the input image to RgbaImage format and retrieve its raw data, width, and height
    let rgba = image.to_rgba8().into_raw();
    let width = image.width() as usize;
    let height = image.height() as usize;

    // Compute the ThumbHash of the input image
    let thumb_hash = rgba_to_thumb_hash(width, height, &rgba);

    // Convert the ThumbHash back to RgbaImage format
    let (_w, _h, rgba2) = thumb_hash_to_rgba(&thumb_hash).unwrap();

    // Create a new file to store the output image
    let output_file = "output.png";
    let file = std::fs::File::create(output_file)?;

    // Initialize a PNG encoder and write the output image to the file
    let encoder = PngEncoder::new(file);
    encoder
        .write_image(
            &rgba2, 
            _w as u32, 
            _h as u32, 
            image::ColorType::Rgba8)
        ?;
    Ok(())
}
