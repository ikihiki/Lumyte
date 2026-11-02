const path = require("node:path");
const { createRules } = require("./adr-checks.cjs");

module.exports = createRules({
  repositoryRoot: path.resolve(__dirname, "../.."),
  legacy: require("./adr-legacy.json"),
});
