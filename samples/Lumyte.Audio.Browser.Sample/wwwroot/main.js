import { dotnet } from "./_framework/dotnet.js";
const button = document.getElementById("start");
const result = document.getElementById("result");
try {
    const runtime = await dotnet.create();
    await runtime.runMain();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const program = exports.Lumyte.Audio.Browser.Sample.Program;
    button.disabled = false;
    button.onclick = async () => {
        button.disabled = true;
        try {
            await program.RunAsync();
            result.textContent = program.GetReport();
            result.dataset.result = "passed";
        } catch (error) {
            result.textContent = String(error);
            result.dataset.result = "failed";
        }
    };
} catch (error) {
    result.textContent = String(error);
    result.dataset.result = "failed";
}
