package thumbhash

import (
	"fmt"
	"math"
)

// RGBAToThumbHash encodes an RGBA image to a ThumbHash.
// RGB should not be premultiplied by A.
//
// @param w    The width of the input image. Must be ≤100px.
// @param h    The height of the input image. Must be ≤100px.
// @param rgba The pixels in the input image, row-by-row. Must have w*h*4 elements.
// @return The ThumbHash as a byte array.
func RGBAToThumbHash(w, h int, rgba []byte) []byte {
	// Encoding an image larger than 100x100 is slow with no benefit
	if w > 100 || h > 100 {
		panic(fmt.Sprintf("%dx%d doesn't fit in 100x100", w, h))
	}

	// Determine the average color
	var avg_r, avg_g, avg_b, avg_a float64

	for i, j := 0, 0; i < w*h; i, j = i+1, j+4 {
		alpha := float64((rgba[j+3] & 255) / 255.0)
		avg_r += alpha / 255.0 * float64(rgba[j]&255)
		avg_g += alpha / 255.0 * float64(rgba[j+1]&255)
		avg_b += alpha / 255.0 * float64(rgba[j+2]&255)
		avg_a += alpha
	}
	if avg_a > 0 {
		avg_r /= avg_a
		avg_g /= avg_a
		avg_b /= avg_a
	}

	hasAlpha := avg_a < float64(w*h)
	l_limit := ter(hasAlpha, 5, 7) // Use fewer luminance bits if there's alpha
	lx := max(1.0, math.Round(float64((l_limit*w)/max(w, h))))
	ly := max(1.0, math.Round(float64((l_limit*h)/max(w, h))))
	l := make([]float64, w*h) // luminance
	p := make([]float64, w*h) // yellow - blue
	q := make([]float64, w*h) // red - green
	a := make([]float64, w*h) // alpha

	// Convert the image from RGBA to LPQA (composite atop the average color)
	for i, j := 0, 0; i < w*h; i, j = i+1, j+4 {
		alpha := float64(rgba[j+3]&255) / 255.0
		r := avg_r*(1.0-alpha) + alpha/255.0*float64(rgba[j]&255)
		g := avg_g*(1.0-alpha) + alpha/255.0*float64(rgba[j+1]&255)
		b := avg_b*(1.0-alpha) + alpha/255.0*float64(rgba[j+2]&255)
		l[i] = (r + g + b) / 3.0
		p[i] = (r+g)/2.0 - b
		q[i] = r - g
		a[i] = alpha
	}

	// Encode using the DCT into DC (constant) and normalized AC (varying) terms
	l_channel := newChannel(max(3, int(lx)), max(3, int(ly))).encode(w, h, l)
	p_channel := newChannel(3, 3).encode(w, h, p)
	q_channel := newChannel(3, 3).encode(w, h, q)
	a_channel := ter(hasAlpha, newChannel(5, 5).encode(w, h, a), Channel{})

	// Write the constants
	isLandscape := w > h
	header24 := int(math.Round(63.0*l_channel.dc)) |
		(int(math.Round(31.5+31.5*p_channel.dc)) << 6) |
		(int(math.Round(31.5+31.5*q_channel.dc)) << 12) |
		(int(math.Round(31.0*l_channel.scale)) << 18) |
		(ter(hasAlpha, 1<<23, 0))
	header16 := int(ter(isLandscape, ly, lx)) |
		(int(math.Round(63.0*float64(p_channel.scale))) << 3) |
		(int(math.Round(63.0*q_channel.scale)) << 9) |
		(ter(isLandscape, 1<<15, 0))
	ac_start := ter(hasAlpha, 6, 5)
	ac_count := len(l_channel.ac) + len(p_channel.ac) + len(q_channel.ac) + (ter(hasAlpha, len(a_channel.ac), 0))
	hash := make([]byte, ac_start+(ac_count+1)/2)
	hash[0] = byte(header24)
	hash[1] = byte((header24 >> 8))
	hash[2] = byte((header24 >> 16))
	hash[3] = byte(header16)
	hash[4] = byte((header16 >> 8))
	if hasAlpha {
		hash[5] = byte(math.Round(15.0*float64(a_channel.dc))) |
			byte(math.Round(15.0*float64(a_channel.scale)))<<4
	}

	// Write the varying factors
	var ac_index int
	ac_index = l_channel.writeTo(hash, ac_start, ac_index)
	ac_index = p_channel.writeTo(hash, ac_start, ac_index)
	ac_index = q_channel.writeTo(hash, ac_start, ac_index)
	if hasAlpha {
		a_channel.writeTo(hash, ac_start, ac_index)
	}
	return hash
}

// ThumbHashToRGBA decodes a ThumbHash to an RGBA image.
// RGB is not be premultiplied by A.
//
// @param hash The bytes of the ThumbHash.
// @return The width, height, and pixels of the rendered placeholder image.
func ThumbHashToRGBA(hash []byte) Image {
	// Read the constants
	header24 := int(hash[0]&255) | int(hash[1]&255)<<8 | int(hash[2]&255)<<16
	header16 := int(hash[3]&255) | int(hash[4]&255)<<8
	l_dc := float64((header24 & 63) / 63.0)
	p_dc := float64((header24>>6)&63)/31.5 - 1.0
	q_dc := float64((header24>>12)&63)/31.5 - 1.0
	l_scale := float64(((header24 >> 18) & 31) / 31.0)
	hasAlpha := (header24 >> 23) != 0
	p_scale := float64(((header16 >> 3) & 63) / 63.0)
	q_scale := float64(((header16 >> 9) & 63) / 63.0)
	isLandscape := (header16 >> 15) != 0
	lx := max(3, ter(isLandscape, ter(hasAlpha, 5, 7), int(header16&7)))
	ly := max(3, ter(isLandscape, int(header16&7), ter(hasAlpha, 5, 7)))
	a_dc := ter(hasAlpha, float64(hash[5]&15)/15.0, 1.0)
	a_scale := float64(((hash[5] >> 4) & 15) / 15.0)

	// Read the varying factors (boost saturation by 1.25x to compensate for quantization)
	ac_start := ter(hasAlpha, 6, 5)
	ac_index := 0
	l_channel := newChannel(lx, ly)
	p_channel := newChannel(3, 3)
	q_channel := newChannel(3, 3)
	a_channel := Channel{}
	ac_index = l_channel.decode(hash, ac_start, ac_index, l_scale)
	ac_index = p_channel.decode(hash, ac_start, ac_index, p_scale*1.25)
	ac_index = q_channel.decode(hash, ac_start, ac_index, q_scale*1.25)
	if hasAlpha {
		a_channel = newChannel(5, 5)
		a_channel.decode(hash, ac_start, ac_index, a_scale)
	}
	l_ac := l_channel.ac
	p_ac := p_channel.ac
	q_ac := q_channel.ac
	var a_ac []float64
	if hasAlpha {
		a_ac = a_channel.ac
	}

	// Decode using the DCT into RGB
	ratio := ThumbHashToApproximateAspectRatio(hash)
	w := int(math.Round(ter(ratio > 1.0, 32.0, 32.0*ratio)))
	h := int(math.Round(ter(ratio > 1.0, 32.0/ratio, 32.0)))
	rgba := make([]byte, w*h*4)
	cx_stop := max(lx, ter(hasAlpha, 5, 3))
	cy_stop := max(ly, ter(hasAlpha, 5, 3))
	fx := make([]float64, cx_stop)
	fy := make([]float64, cy_stop)
	for y, i := 0, 0; y < h; y++ {
		for x := 0; x < w; x, i = x+1, i+4 {
			l := l_dc
			p := p_dc
			q := q_dc
			a := a_dc

			// Precompute the coefficients
			for cx := 0; cx < cx_stop; cx++ {
				fx[cx] = math.Cos(math.Pi / float64(w) * (float64(x) + 0.5) * float64(cx))
			}
			for cy := 0; cy < cy_stop; cy++ {
				fy[cy] = math.Cos(math.Pi / float64(h) * (float64(y) + 0.5) * float64(cy))
			}

			// Decode L
			for cy, j := 0, 0; cy < ly; cy++ {
				fy2 := fy[cy] * 2.0
				for cx := ter(cy > 0, 0, 1); cx*ly < lx*(ly-cy); cx, j = cx+1, j+1 {
					l += l_ac[j] * fx[cx] * fy2
				}
			}

			// Decode P and Q
			for cy, j := 0, 0; cy < 3; cy++ {
				fy2 := fy[cy] * 2.0
				for cx := ter(cy > 0, 0, 1); cx < 3-cy; cx, j = cx+1, j+1 {
					f := fx[cx] * fy2
					p += p_ac[j] * f
					q += q_ac[j] * f
				}
			}

			// Decode A
			if hasAlpha {

				for cy, j := 0, 0; cy < 5; cy++ {
					fy2 := fy[cy] * 2.0
					for cx := ter(cy > 0, 0, 1); cx < 5-cy; cx, j = cx+1, j+1 {
						a += a_ac[j] * fx[cx] * fy2
					}
				}
			}

			// Convert to RGB
			b := l - 2.0/3.0*p
			r := (3.0*l - b + q) / 2.0
			g := r - q
			rgba[i] = byte(math.Max(0, math.Round(255.0*math.Min(1, r))))
			rgba[i+1] = byte(math.Max(0, math.Round(255.0*math.Min(1, g))))
			rgba[i+2] = byte(math.Max(0, math.Round(255.0*math.Min(1, b))))
			rgba[i+3] = byte(math.Max(0, math.Round(255.0*math.Min(1, a))))
		}
	}
	return newImage(w, h, rgba)
}

// ThumbHashToAverageRGBA extracts the average color from a ThumbHash.
// RGB is not be premultiplied by A.
//
// @param hash The bytes of the ThumbHash.
// @return The RGBA values for the average color. Each value ranges from 0 to 1.
func ThumbHashToAverageRGBA(hash []byte) RGBA {
	header := (int(hash[0]) & 255) | ((int(hash[1]) & 255) << 8) | ((int(hash[2]) & 255) << 16)
	l := float64(header&63) / 63.0
	p := float64((header>>6)&63)/31.5 - 1.0
	q := float64((header>>12)&63)/31.5 - 1.0
	hasAlpha := (header >> 23) != 0
	a := 1.0
	if hasAlpha {
		a = float64(hash[5]&15) / 15.0
	}
	b := l - 2.0/3.0*p
	r := (3.0*l - b + q) / 2.0
	g := r - q
	return newRGBA(
		math.Max(0, math.Min(1, r)),
		math.Max(0, math.Min(1, g)),
		math.Max(0, math.Min(1, b)),
		a)
}

// ThumbHashToApproximateAspectRatio Extracts the approximate aspect ratio of the original image.
//
// @param hash The bytes of the ThumbHash.
// @return The approximate aspect ratio (i.e. width / height).
func ThumbHashToApproximateAspectRatio(hash []byte) float64 {
	header := hash[3]
	hasAlpha := (hash[2] & 0x80) != 0
	isLandscape := (hash[4] & 0x80) != 0
	lx := ter(isLandscape, ter(hasAlpha, 5, 7), int(header&7))
	ly := ter(isLandscape, int(header&7), ter(hasAlpha, 5, 7))
	return float64(lx) / float64(ly)
}

type Image struct {
	width  int
	height int
	rgba   []byte
}

func newImage(width, height int, rgba []byte) Image {
	return Image{
		width:  width,
		height: height,
		rgba:   rgba,
	}
}

type RGBA struct {
	r float64
	g float64
	b float64
	a float64
}

func newRGBA(r, g, b, a float64) RGBA {
	return RGBA{
		r: r,
		g: g,
		b: b,
		a: a,
	}
}

type Channel struct {
	nx    int
	ny    int
	dc    float64
	ac    []float64
	scale float64
}

func newChannel(nx, ny int) Channel {
	this := Channel{
		nx: nx,
		ny: ny,
	}

	var n int
	for cy := 0; cy < ny; cy++ {
		for cx := ter(cy > 0, 0, 1); cx*ny < nx*(ny-cy); cx++ {
			n++
		}
	}
	this.ac = make([]float64, n)
	return this
}

func (this Channel) encode(w, h int, channel []float64) Channel {
	var n int
	fx := make([]float64, w)
	for cy := 0; cy < this.ny; cy++ {
		for cx := 0; cx*this.ny < this.nx*(this.ny-cy); cx++ {
			var f float64
			for x := 0; x < w; x++ {
				fx[x] = math.Cos(math.Pi / float64(w*cx) * (float64(x) + 0.5))
			}
			for y := 0; y < h; y++ {
				fy := math.Cos(math.Pi / float64(h*cy) * (float64(y) + 0.5))
				for x := 0; x < w; x++ {
					f += channel[x+y*w] * fx[x] * fy
				}
			}
			f /= float64(w * h)
			if cx > 0 || cy > 0 {
				this.ac[n] = f
				n++
				this.scale = math.Max(this.scale, math.Abs(f))
			} else {
				this.dc = f
			}
		}
	}

	if this.scale > 0 {
		for i := 0; i < len(this.ac); i++ {
			this.ac[i] = 0.5 + 0.5/this.scale*this.ac[i]
		}
	}
	return this
}

func (this Channel) decode(hash []byte, start, index int, scale float64) int {
	for i := 0; i < len(this.ac); i++ {
		data := hash[start+(index>>1)] >> ((index & 1) << 2)
		this.ac[i] = ((float64)(data&15)/7.5 - 1.0) * scale
		index++
	}
	return index
}

func (this Channel) writeTo(hash []byte, start, index int) int {
	for _, v := range this.ac {
		hash[start+(index>>1)] |= byte(math.Round(15*v)) << ((index & 1) << 2)
		index++
	}
	return index
}

type ordered interface {
	float64 | int
}

func max[T ordered](a, b T) T {
	if a > b {
		return a
	}
	return b
}

func ter[T any](f bool, a, b T) T {
	if f {
		return a
	}
	return b
}
