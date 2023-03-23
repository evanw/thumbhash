import UIKit

class ViewController: UIViewController {
  override func viewDidAppear(_ animated: Bool) {
    super.viewDidAppear(animated)

    let image = UIImage(imageLiteralResourceName: "flower.jpg")

    // Image to ThumbHash
    let thumbHash = imageToThumbHash(image: image)

    // ThumbHash to image
    let placeholder = thumbHashToImage(hash: thumbHash)

    // Simulate setting the placeholder first, then the full image loading later on
    let view = UIImageView(image: placeholder)
    view.contentMode = .scaleAspectFill
    view.clipsToBounds = true
    view.frame = CGRect(x: 20, y: self.view.safeAreaInsets.top + 20, width: 150, height: 200)
    self.view.addSubview(view)

    DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
      view.image = image
    }
  }
}
