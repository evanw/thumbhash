# ThumbHash

 A very compact representation of a placeholder for an image. Store it inline with your data and show it while the real image is loading for a smoother loading experience. It's similar to [BlurHash](https://github.com/woltapp/blurhash) but with the following advantages:

* Encodes more detail in the same space
* Also encodes the aspect ratio
* Gives more accurate colors
* Supports images with alpha

Despite doing all of these additional things, the code for ThumbHash is still similar in complexity to the code for BlurHash. One potential drawback compared to BlurHash is that the parameters of the algorithm are not configurable (everything is automatically configured).

A demo and more information is available here: https://evanw.github.io/thumbhash/.

## Implementations

This repo contains implementations for the following languages:

* [JavaScript](./js)
* [Rust](./rust)
* [Swift](./swift)
* [Java](./java)

These additional implementations also exist outside of this repo:

* Go: https://github.com/galdor/go-thumbhash

_If you want to add your own implementation here, you can send a PR that puts a link to your implementation in this README._
