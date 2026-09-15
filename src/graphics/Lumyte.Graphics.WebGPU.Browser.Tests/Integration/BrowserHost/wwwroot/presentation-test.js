let latest;
const getCurrentTexture = GPUCanvasContext.prototype.getCurrentTexture;
GPUCanvasContext.prototype.getCurrentTexture = function () {
    const texture = getCurrentTexture.call(this);
    if (this.canvas.id === "presentation") {
        // Snapshot the actual canvas in the submission turn, before automatic expiry.
        queueMicrotask(snapshotCanvas);
    }
    return texture;
};
function snapshotCanvas() {
    const source = document.getElementById("presentation");
    const capture = document.createElement("canvas");
    capture.width = source.width;
    capture.height = source.height;
    const context = capture.getContext("2d");
    context.drawImage(source, 0, 0);
    const pixels = Array.from(context.getImageData(0, 0, capture.width, capture.height).data);
    latest = JSON.stringify({ width: capture.width, height: capture.height, pixels });
}
export function captureCanvas() { return Promise.resolve(latest); }
