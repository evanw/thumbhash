/**
 * Encodes an RGBA image to a ThumbHash. RGB should not be premultiplied by A.
 *
 * @param w The width of the input image. Must be ≤100px.
 * @param h The height of the input image. Must be ≤100px.
 * @param rgba The pixels in the input image, row-by-row. Must have w*h*4 elements.
 * @returns The ThumbHash as a Uint8Array.
 */
export function rgbaToThumbHash(w: number, h: number, rgba: ArrayLike<number>): Uint8Array

/**
 * Decodes a ThumbHash to an RGBA image. RGB is not be premultiplied by A.
 *
 * @param hash The bytes of the ThumbHash.
 * @returns The width, height, and pixels of the rendered placeholder image.
 */
export function thumbHashToRGBA(hash: ArrayLike<number>): { w: number, h: number, rgba: Uint8Array }

/**
 * Extracts the average color from a ThumbHash. RGB is not be premultiplied by A.
 *
 * @param hash The bytes of the ThumbHash.
 * @returns The RGBA values for the average color. Each value ranges from 0 to 1.
 */
export function thumbHashToAverageRGBA(hash: ArrayLike<number>): { r: number, g: number, b: number, a: number }

/**
 * Extracts the approximate aspect ratio of the original image.
 *
 * @param hash The bytes of the ThumbHash.
 * @returns The approximate aspect ratio (i.e. width / height).
 */
export function thumbHashToApproximateAspectRatio(hash: ArrayLike<number>): number

/**
 * Encodes an RGBA image to a PNG data URL. RGB should not be premultiplied by
 * A. This is optimized for speed and simplicity and does not optimize for size
 * at all. This doesn't do any compression (all values are stored uncompressed).
 *
 * @param w The width of the input image. Must be ≤100px.
 * @param h The height of the input image. Must be ≤100px.
 * @param rgba The pixels in the input image, row-by-row. Must have w*h*4 elements.
 * @returns A data URL containing a PNG for the input image.
 */
export function rgbaToDataURL(w: number, h: number, rgba: ArrayLike<number>): string

/**
 * Decodes a ThumbHash to a PNG data URL. This is a convenience function that
 * just calls "thumbHashToRGBA" followed by "rgbaToDataURL".
 *
 * @param hash The bytes of the ThumbHash.
 * @returns A data URL containing a PNG for the rendered ThumbHash.
 */
export function thumbHashToDataURL(hash: ArrayLike<number>): string
