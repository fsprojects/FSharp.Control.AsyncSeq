const path = require("path");

module.exports = {
    allFiles: true,
    entry: path.join(__dirname, "./FSharp.Control.AsyncSeq.Tests.fsproj"),
    outDir: path.join(__dirname, "../dist/tests"),
    babel: {
        plugins: [
            "@babel/plugin-transform-modules-commonjs"
        ],
        sourceMaps: "inline"
    },
    fable: {
        typedArrays: false
    }
};
