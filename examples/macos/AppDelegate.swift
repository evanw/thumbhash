import Cocoa

@main
class AppDelegate: NSObject, NSApplicationDelegate {
  @IBOutlet var window: NSWindow!

  func applicationDidFinishLaunching(_ aNotification: Notification) {
    let image = Bundle.main.image(forResource: "flower.jpg")!

    // Image to ThumbHash
    let thumbHash = imageToThumbHash(image: image)

    // ThumbHash to image
    let placeholder = thumbHashToImage(hash: thumbHash)

    // Simulate setting the placeholder first, then the full image loading later on
    let view = NSImageView(image: placeholder)
    view.imageScaling = .scaleProportionallyUpOrDown
    view.frame = NSRect(x: 20, y: 20, width: 150, height: 200)
    window.contentView = FlippedView()
    window.contentView!.addSubview(view)

    DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
      view.image = image
    }
  }
}

private final class FlippedView : NSView {
  override var isFlipped: Bool { true }
}
