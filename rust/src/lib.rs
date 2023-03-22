use std::f32::consts::PI;
use std::io::Read;

/// Encodes an RGBA image to a ThumbHash. RGB should not be premultiplied by A.
///
/// * `w`: The width of the input image. Must be ≤100px.
/// * `h`: The height of the input image. Must be ≤100px.
/// * `rgba`: The pixels in the input image, row-by-row. Must have `w*h*4` elements.
pub fn rgba_to_thumb_hash(w: usize, h: usize, rgba: &[u8]) -> Vec<u8> {
    // Encoding an image larger than 100x100 is slow with no benefit
    assert!(w <= 100 && h <= 100);
    assert_eq!(rgba.len(), w * h * 4);

    // Determine the average color
    let mut avg_r = 0.0;
    let mut avg_g = 0.0;
    let mut avg_b = 0.0;
    let mut avg_a = 0.0;
    for rgba in rgba.chunks_exact(4) {
        let alpha = rgba[3] as f32 / 255.0;
        avg_r += alpha / 255.0 * rgba[0] as f32;
        avg_g += alpha / 255.0 * rgba[1] as f32;
        avg_b += alpha / 255.0 * rgba[2] as f32;
        avg_a += alpha;
    }
    if avg_a > 0.0 {
        avg_r /= avg_a;
        avg_g /= avg_a;
        avg_b /= avg_a;
    }

    let has_alpha = avg_a < (w * h) as f32;
    let l_limit = if has_alpha { 5 } else { 7 }; // Use fewer luminance bits if there's alpha
    let lx = (((l_limit * w) as f32 / w.max(h) as f32).round() as usize).max(1);
    let ly = (((l_limit * h) as f32 / w.max(h) as f32).round() as usize).max(1);
    let mut l = Vec::with_capacity(w * h); // luminance
    let mut p = Vec::with_capacity(w * h); // yellow - blue
    let mut q = Vec::with_capacity(w * h); // red - green
    let mut a = Vec::with_capacity(w * h); // alpha

    // Convert the image from RGBA to LPQA (composite atop the average color)
    for rgba in rgba.chunks_exact(4) {
        let alpha = rgba[3] as f32 / 255.0;
        let r = avg_r * (1.0 - alpha) + alpha / 255.0 * rgba[0] as f32;
        let g = avg_g * (1.0 - alpha) + alpha / 255.0 * rgba[1] as f32;
        let b = avg_b * (1.0 - alpha) + alpha / 255.0 * rgba[2] as f32;
        l.push((r + g + b) / 3.0);
        p.push((r + g) / 2.0 - b);
        q.push(r - g);
        a.push(alpha);
    }

    // Encode using the DCT into DC (constant) and normalized AC (varying) terms
    let encode_channel = |channel: &[f32], nx: usize, ny: usize| -> (f32, Vec<f32>, f32) {
        let mut dc = 0.0;
        let mut ac = Vec::with_capacity(nx * ny / 2);
        let mut scale = 0.0;
        let mut fx = [0.0].repeat(w);
        for cy in 0..ny {
            let mut cx = 0;
            while cx * ny < nx * (ny - cy) {
                let mut f = 0.0;
                for x in 0..w {
                    fx[x] = (PI / w as f32 * cx as f32 * (x as f32 + 0.5)).cos();
                }
                for y in 0..h {
                    let fy = (PI / h as f32 * cy as f32 * (y as f32 + 0.5)).cos();
                    for x in 0..w {
                        f += channel[x + y * w] * fx[x] * fy;
                    }
                }
                f /= (w * h) as f32;
                if cx > 0 || cy > 0 {
                    ac.push(f);
                    scale = f.abs().max(scale);
                } else {
                    dc = f;
                }
                cx += 1;
            }
        }
        if scale > 0.0 {
            for ac in &mut ac {
                *ac = 0.5 + 0.5 / scale * *ac;
            }
        }
        (dc, ac, scale)
    };
    let (l_dc, l_ac, l_scale) = encode_channel(&l, lx.max(3), ly.max(3));
    let (p_dc, p_ac, p_scale) = encode_channel(&p, 3, 3);
    let (q_dc, q_ac, q_scale) = encode_channel(&q, 3, 3);
    let (a_dc, a_ac, a_scale) = if has_alpha {
        encode_channel(&a, 5, 5)
    } else {
        (1.0, Vec::new(), 1.0)
    };

    // Write the constants
    let is_landscape = w > h;
    let header24 = (63.0 * l_dc).round() as u32
        | (((31.5 + 31.5 * p_dc).round() as u32) << 6)
        | (((31.5 + 31.5 * q_dc).round() as u32) << 12)
        | (((31.0 * l_scale).round() as u32) << 18)
        | if has_alpha { 1 << 23 } else { 0 };
    let header16 = (if is_landscape { ly } else { lx }) as u16
        | (((63.0 * p_scale).round() as u16) << 3)
        | (((63.0 * q_scale).round() as u16) << 9)
        | if is_landscape { 1 << 15 } else { 0 };
    let mut hash = Vec::with_capacity(25);
    hash.extend_from_slice(&[
        (header24 & 255) as u8,
        ((header24 >> 8) & 255) as u8,
        (header24 >> 16) as u8,
        (header16 & 255) as u8,
        (header16 >> 8) as u8,
    ]);
    let mut is_odd = false;
    if has_alpha {
        hash.push((15.0 * a_dc).round() as u8 | (((15.0 * a_scale).round() as u8) << 4));
    }

    // Write the varying factors
    for ac in [l_ac, p_ac, q_ac] {
        for f in ac {
            let u = (15.0 * f).round() as u8;
            if is_odd {
                *hash.last_mut().unwrap() |= u << 4;
            } else {
                hash.push(u);
            }
            is_odd = !is_odd;
        }
    }
    if has_alpha {
        for f in a_ac {
            let u = (15.0 * f).round() as u8;
            if is_odd {
                *hash.last_mut().unwrap() |= u << 4;
            } else {
                hash.push(u);
            }
            is_odd = !is_odd;
        }
    }
    hash
}

fn read_byte(bytes: &mut &[u8]) -> Result<u8, ()> {
    let mut byte = [0; 1];
    bytes.read_exact(&mut byte).map_err(|_| ())?;
    Ok(byte[0])
}

/// Decodes a ThumbHash to an RGBA image.
///
/// RGB is not be premultiplied by A. Returns the width, height, and pixels of
/// the rendered placeholder image. An error will be returned if the input is
/// too short.
pub fn thumb_hash_to_rgba(mut hash: &[u8]) -> Result<(usize, usize, Vec<u8>), ()> {
    let ratio = thumb_hash_to_approximate_aspect_ratio(hash)?;

    // Read the constants
    let header24 = read_byte(&mut hash)? as u32
        | ((read_byte(&mut hash)? as u32) << 8)
        | ((read_byte(&mut hash)? as u32) << 16);
    let header16 = read_byte(&mut hash)? as u16 | ((read_byte(&mut hash)? as u16) << 8);
    let l_dc = (header24 & 63) as f32 / 63.0;
    let p_dc = ((header24 >> 6) & 63) as f32 / 31.5 - 1.0;
    let q_dc = ((header24 >> 12) & 63) as f32 / 31.5 - 1.0;
    let l_scale = ((header24 >> 18) & 31) as f32 / 31.0;
    let has_alpha = (header24 >> 23) != 0;
    let p_scale = ((header16 >> 3) & 63) as f32 / 63.0;
    let q_scale = ((header16 >> 9) & 63) as f32 / 63.0;
    let is_landscape = (header16 >> 15) != 0;
    let l_max = if has_alpha { 5 } else { 7 };
    let lx = 3.max(if is_landscape { l_max } else { header16 & 7 }) as usize;
    let ly = 3.max(if is_landscape { header16 & 7 } else { l_max }) as usize;
    let (a_dc, a_scale) = if has_alpha {
        let header8 = read_byte(&mut hash)?;
        ((header8 & 15) as f32 / 15.0, (header8 >> 4) as f32 / 15.0)
    } else {
        (1.0, 1.0)
    };

    // Read the varying factors (boost saturation by 1.25x to compensate for quantization)
    let mut prev_bits = None;
    let mut decode_channel = |nx: usize, ny: usize, scale: f32| -> Result<Vec<f32>, ()> {
        let mut ac = Vec::with_capacity(nx * ny);
        for cy in 0..ny {
            let mut cx = if cy > 0 { 0 } else { 1 };
            while cx * ny < nx * (ny - cy) {
                let bits = if let Some(bits) = prev_bits {
                    prev_bits = None;
                    bits
                } else {
                    let bits = read_byte(&mut hash)?;
                    prev_bits = Some(bits >> 4);
                    bits & 15
                };
                ac.push((bits as f32 / 7.5 - 1.0) * scale);
                cx += 1;
            }
        }
        Ok(ac)
    };
    let l_ac = decode_channel(lx, ly, l_scale)?;
    let p_ac = decode_channel(3, 3, p_scale * 1.25)?;
    let q_ac = decode_channel(3, 3, q_scale * 1.25)?;
    let a_ac = if has_alpha {
        decode_channel(5, 5, a_scale)?
    } else {
        Vec::new()
    };

    // Decode using the DCT into RGB
    let (w, h) = if ratio > 1.0 {
        (32, (32.0 / ratio).round() as usize)
    } else {
        ((32.0 * ratio).round() as usize, 32)
    };
    let mut rgba = Vec::with_capacity(w * h * 4);
    let mut fx = [0.0].repeat(7);
    let mut fy = [0.0].repeat(7);
    for y in 0..h {
        for x in 0..w {
            let mut l = l_dc;
            let mut p = p_dc;
            let mut q = q_dc;
            let mut a = a_dc;

            // Precompute the coefficients
            for cx in 0..lx.max(if has_alpha { 5 } else { 3 }) {
                fx[cx] = (PI / w as f32 * (x as f32 + 0.5) * cx as f32).cos();
            }
            for cy in 0..ly.max(if has_alpha { 5 } else { 3 }) {
                fy[cy] = (PI / h as f32 * (y as f32 + 0.5) * cy as f32).cos();
            }

            // Decode L
            let mut j = 0;
            for cy in 0..ly {
                let mut cx = if cy > 0 { 0 } else { 1 };
                let fy2 = fy[cy] * 2.0;
                while cx * ly < lx * (ly - cy) {
                    l += l_ac[j] * fx[cx] * fy2;
                    j += 1;
                    cx += 1;
                }
            }

            // Decode P and Q
            let mut j = 0;
            for cy in 0..3 {
                let mut cx = if cy > 0 { 0 } else { 1 };
                let fy2 = fy[cy] * 2.0;
                while cx < 3 - cy {
                    let f = fx[cx] * fy2;
                    p += p_ac[j] * f;
                    q += q_ac[j] * f;
                    j += 1;
                    cx += 1;
                }
            }

            // Decode A
            if has_alpha {
                let mut j = 0;
                for cy in 0..5 {
                    let mut cx = if cy > 0 { 0 } else { 1 };
                    let fy2 = fy[cy] * 2.0;
                    while cx < 5 - cy {
                        a += a_ac[j] * fx[cx] * fy2;
                        j += 1;
                        cx += 1;
                    }
                }
            }

            // Convert to RGB
            let b = l - 2.0 / 3.0 * p;
            let r = (3.0 * l - b + q) / 2.0;
            let g = r - q;
            rgba.extend_from_slice(&[
                (r.clamp(0.0, 1.0) * 255.0) as u8,
                (g.clamp(0.0, 1.0) * 255.0) as u8,
                (b.clamp(0.0, 1.0) * 255.0) as u8,
                (a.clamp(0.0, 1.0) * 255.0) as u8,
            ]);
        }
    }
    Ok((w, h, rgba))
}

/// Extracts the average color from a ThumbHash.
///
/// Returns the RGBA values where each value ranges from 0 to 1. RGB is not be
/// premultiplied by A. An error will be returned if the input is too short.
pub fn thumb_hash_to_average_rgba(hash: &[u8]) -> Result<(f32, f32, f32, f32), ()> {
    if hash.len() < 5 {
        return Err(());
    }
    let header = hash[0] as u32 | ((hash[1] as u32) << 8) | ((hash[2] as u32) << 16);
    let l = (header & 63) as f32 / 63.0;
    let p = ((header >> 6) & 63) as f32 / 31.5 - 1.0;
    let q = ((header >> 12) & 63) as f32 / 31.5 - 1.0;
    let has_alpha = (header >> 23) != 0;
    let a = if has_alpha {
        (hash[5] & 15) as f32 / 15.0
    } else {
        1.0
    };
    let b = l - 2.0 / 3.0 * p;
    let r = (3.0 * l - b + q) / 2.0;
    let g = r - q;
    Ok((r.clamp(0.0, 1.0), g.clamp(0.0, 1.0), b.clamp(0.0, 1.0), a))
}

/// Extracts the approximate aspect ratio of the original image.
///
/// An error will be returned if the input is too short.
pub fn thumb_hash_to_approximate_aspect_ratio(hash: &[u8]) -> Result<f32, ()> {
    if hash.len() < 5 {
        return Err(());
    }
    let has_alpha = (hash[2] & 0x80) != 0;
    let l_max = if has_alpha { 5 } else { 7 };
    let l_min = hash[3] & 7;
    let is_landscape = (hash[4] & 0x80) != 0;
    let lx = if is_landscape { l_max } else { l_min };
    let ly = if is_landscape { l_min } else { l_max };
    Ok(lx as f32 / ly as f32)
}
