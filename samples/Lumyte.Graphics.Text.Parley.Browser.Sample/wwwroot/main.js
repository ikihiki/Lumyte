import { dotnet } from "./_framework/dotnet.js";

const output = document.getElementById("report");
try {
    const runtime = await dotnet.create();
    const exitCode = await runtime.runMain();
    if (exitCode !== 0) {
        throw new Error(`.NET exited with code ${exitCode}`);
    }
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const program = exports.Lumyte.Graphics.Text.Parley.Browser.Sample.Program;
    const bytes = Uint8ClampedArray.from(atob(program.GetPixelsBase64()), character => character.charCodeAt(0));
    document.getElementById("drawing").getContext("2d").putImageData(new ImageData(bytes, 900, 1000), 0, 0);
    output.textContent = program.GetReport();
    output.dataset.result = "passed";
} catch (error) {
    output.textContent = String(error);
    output.dataset.result = "failed";
    console.error(error);
}
