// This is a quick experiment to represent image thumbnails as very small WebP
// files (potato quality): https://knowyourmeme.com/memes/recorded-with-a-potato.
// The "hash" is just the VP8 chunk in a 16x16 WebP file at 0% quality.

export function encode(img) {
  let canvas = document.createElement('canvas')
  let c = canvas.getContext('2d')
  canvas.width = canvas.height = 16
  c.fillStyle = '#FFF'
  c.fillRect(0, 0, 16, 16)
  c.drawImage(img, 0, 0, 16, 16)
  let url = canvas.toDataURL('image/webp', 0)
  let prefix = 'data:image/webp;base64,'
  if (!url.startsWith(prefix)) return null

  // Crack open the WebP
  let bytes = new Uint8Array(atob(url.slice(prefix.length)).split('').map(x => x.charCodeAt()))
  let offset = 12
  while (offset < bytes.length) {
    let chunkTag = String.fromCharCode(...bytes.subarray(offset, offset + 4))
    let chunkLength = bytes[offset + 4] | (bytes[offset + 5] << 8)
    if (chunkTag === 'VP8 ') return new Uint8Array(bytes.subarray(offset + 8, offset + 8 + chunkLength))
    offset += 8 + chunkLength
  }
  return null
}

export async function decode(hash) {
  // Reconstruct the WebP file
  let bytes = [82, 73, 70, 70, 0, 0, 0, 0, 87, 69, 66, 80, 86, 80, 56, 32, hash.length, 0, 0, 0, ...hash]
  if (hash.length & 1) bytes.push(0)
  bytes[4] = bytes.length - 8

  // Decode the WebP
  let img = new Image
  img.src = 'data:image/webp;base64,' + btoa(String.fromCharCode(...bytes))
  await new Promise((resolve, reject) => {
    img.onload = resolve
    img.onerror = reject
  })
  let canvas = document.createElement('canvas')
  let c = canvas.getContext('2d')
  canvas.width = canvas.height = 16
  c.drawImage(img, 0, 0, 16, 16)
  canvas.style.background = 'red'
  let pixels = c.getImageData(0, 0, 16, 16)

  // Apply a 2D gaussian-like blur
  let data = pixels.data
  let temp = new Uint8Array(16 * 16 * 4)
  for (let blur = 0; blur < 2; blur++) {
    let radius = blur ? 1 : 2
    let diameter = 2 * radius
    let r = 0, g = 0, b = 0, total = 0

    // Horizontal box blur
    for (let y = 0; y < 16; y++) {
      for (let x = 0; x < 16 + diameter; x++) {
        if (x < 16) {
          let i = (x + y * 16) << 2
          r += data[i]
          g += data[i + 1]
          b += data[i + 2]
          total++
        }
        if (x >= radius && x < radius + 16) {
          let i = ((x - radius) * 16 + y) << 2
          temp[i] = r / total
          temp[i + 1] = g / total
          temp[i + 2] = b / total
        }
        if (x >= diameter) {
          let i = ((x - diameter) + y * 16) << 2
          r -= data[i]
          g -= data[i + 1]
          b -= data[i + 2]
          total--
        }
      }
    }

    // Vertical box blur
    for (let x = 0; x < 16; x++) {
      for (let y = 0; y < 16 + diameter; y++) {
        if (y < 16) {
          let i = (x * 16 + y) << 2
          r += temp[i]
          g += temp[i + 1]
          b += temp[i + 2]
          total++
        }
        if (y >= radius && y < radius + 16) {
          let i = (x + (y - radius) * 16) << 2
          data[i] = r / total
          data[i + 1] = g / total
          data[i + 2] = b / total
        }
        if (y >= diameter) {
          let i = (x * 16 + (y - diameter)) << 2
          r -= temp[i]
          g -= temp[i + 1]
          b -= temp[i + 2]
          total--
        }
      }
    }
  }
  c.putImageData(pixels, 0, 0)
  return canvas
}
