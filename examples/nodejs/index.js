import { writeFile } from 'node:fs/promises'
import { rgbaToThumbHash, thumbHashToDataURL, thumbHashToRGBA } from 'thumbhash'
import Jimp from 'jimp'

/**
 * @param {string} path Path or URL to image
 * @returns {Promise<{ext: string, width: number, height: number, data: Buffer}>}
 */
async function getImgData(path) {
   // Read image
   let img = await Jimp.read(path)

   // Resize if necessary
   const { width: w, height: h } = img.bitmap
   if (w > 100 || h > 100) {
      img = img.resize(...(w >= h ? [100, Jimp.AUTO] : [Jimp.AUTO, 100]))
   }

   return { ext: img.getExtension(), ...img.bitmap }
}

// Generate thumbhash
const { width, height, data, ext } = await getImgData('./logo.png')
const hash = rgbaToThumbHash(width, height, data)

// Store dataURL somewhere. Usually it is stored along with the full image url
// to be temporarly rendered like: <img src={dataUrl} ... />
const dataUrl = thumbHashToDataURL(hash)
await writeFile('logo-dataurl.txt', dataUrl)

// Or write thumbhash image to disk
const { w, h, rgba } = thumbHashToRGBA(hash)
new Jimp({ width: w, height: h, data: rgba }, async (err, img) => {
   await img.writeAsync('logo-thumbhash.' + ext)
   if (err) console.log(err)
})
