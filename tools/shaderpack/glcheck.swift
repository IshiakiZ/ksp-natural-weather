// glcheck: compile the vertex and fragment programs of one of this project's shaders (tools/shaderpack/*.glsl) with
// this Mac's own OpenGL, and print what the compiler says. A hand-packed shader that does not compile is drawn by the
// game in solid pink, and the game's log says nothing useful: check here first.
//
//     swiftc -O -o /tmp/glcheck tools/shaderpack/glcheck.swift && /tmp/glcheck tools/shaderpack/volume.glsl
//
// That checks the text as it is written. What goes into a bundle is that text with each texture's uniform given a
// location (make_bundle.py's fixed_locations): tools/shaderpack/glcheck_all.py checks every shader as it is packed,
// and that each bundle holds that very text.
//
// (The text of each program is what lies between "#ifdef VERTEX" or "#ifdef FRAGMENT" and the "#endif" that closes it,
// as make_bundle.py packs it.)
import AppKit
import OpenGL.GL3

func programs(_ text: String) -> [(String, String)] {
    var out: [(String, String)] = []
    let lines = text.components(separatedBy: "\n")
    for name in ["VERTEX", "FRAGMENT"] {
        guard let start = lines.firstIndex(where: { $0.trimmingCharacters(in: .whitespaces) == "#ifdef \(name)" }) else { continue }
        // (to the "#endif" that closes it: a program may have conditions of its own inside it, as every packed fragment
        // program has since each texture's uniform was given a location where the driver takes one. Cut at the first
        // "#ifdef" inside, as this was at first, such a program was passed over without a word.)
        var depth = 1, body: [String] = [], closed = false
        for line in lines[(start + 1)...] {
            let word = line.trimmingCharacters(in: .whitespaces)
            if word.hasPrefix("#if") { depth += 1 }
            else if word.hasPrefix("#endif") { depth -= 1; if depth == 0 { closed = true; break } }
            body.append(line)
        }
        if closed { out.append((name, body.joined(separator: "\n") + "\n")) }
    }
    return out
}

func compile(_ name: String, _ source: String, _ kind: GLenum) -> Bool {
    let shader = glCreateShader(kind)
    source.withCString { pointer in
        var p: UnsafePointer<GLchar>? = pointer
        glShaderSource(shader, 1, &p, nil)
    }
    glCompileShader(shader)
    var ok: GLint = 0, length: GLint = 0
    glGetShaderiv(shader, GLenum(GL_COMPILE_STATUS), &ok)
    glGetShaderiv(shader, GLenum(GL_INFO_LOG_LENGTH), &length)
    if length > 1 {
        var log = [GLchar](repeating: 0, count: Int(length))
        glGetShaderInfoLog(shader, length, nil, &log)
        print("\(name):\n" + String(cString: log))
    }
    print("\(name): " + (ok != 0 ? "compiles" : "DOES NOT COMPILE"))
    return ok != 0
}

let arguments = CommandLine.arguments
guard arguments.count > 1, let text = try? String(contentsOfFile: arguments[1], encoding: .utf8) else {
    print("usage: glcheck file.glsl"); exit(2)
}
let attributes: [NSOpenGLPixelFormatAttribute] = [UInt32(NSOpenGLPFAOpenGLProfile), UInt32(NSOpenGLProfileVersion4_1Core), 0]
guard let format = NSOpenGLPixelFormat(attributes: attributes), let context = NSOpenGLContext(format: format, share: nil) else {
    print("no OpenGL context"); exit(1)
}
context.makeCurrentContext()
var all = true
let found = programs(text)
for (name, source) in found {
    all = compile(name, source, name == "VERTEX" ? GLenum(GL_VERTEX_SHADER) : GLenum(GL_FRAGMENT_SHADER)) && all
}
// (both, or it has not been checked: a program that was not found is not one that compiles)
for name in ["VERTEX", "FRAGMENT"] where !found.contains(where: { $0.0 == name }) {
    print("\(name): NOT FOUND in \(arguments[1])"); all = false
}
exit(all ? 0 : 1)
