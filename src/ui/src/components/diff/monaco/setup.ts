/**
 * The only file in the application that imports Monaco.
 *
 * Everything about this file is shaped by two constraints from Iteration 1 that were set before
 * Monaco existed here, and that Iteration 10 deliberately does not relax:
 *
 * - **The Content-Security-Policy.** `script-src 'self'` with no `'unsafe-eval'`, and no remote
 *   origin of any kind. Monaco is bundled; nothing is fetched. `style-src 'unsafe-inline'` and
 *   `worker-src 'self' blob:` were already granted in `index.html`, with a comment naming Monaco as
 *   the reason — so the policy did not have to change for this iteration, and
 *   `04-shell-guarantees.spec.ts` asks the engine directly rather than taking that on trust.
 * - **`worker.format: 'iife'`** in `vite.config.ts`. WebView2 will not start a module worker served
 *   from the `diffhacker://` scheme, so Monaco's worker is built as a classic one, exactly as the ELK
 *   layout worker is.
 *
 * What is imported is as important as how. Not `monaco-editor`, and not `editor.main`: both pull in
 * the CSS, HTML, JSON and TypeScript *language services*, each with a worker of its own. This product
 * is language-agnostic (§0.2.3) and has no business running a TypeScript compiler to colour a diff.
 * Highlighting comes from `basic-languages` instead, which is Monarch — a tokenizer that runs on the
 * main thread — and whose grammars load on demand, so eighty-one languages cost eighty-one tiny
 * registrations here and one lazy chunk each only if a reviewer opens a file in that language.
 *
 * The one worker that remains is Monaco's own `editor.worker`, which is what computes the diff.
 */
import * as monaco from 'monaco-editor/esm/vs/editor/editor.api.js';

// Editor features — the DiffEditor widget among them — and `registerLanguage`, which every language
// contribution below calls. Importing any one contribution would pull this in anyway; naming it makes
// the dependency visible rather than incidental.
import 'monaco-editor/esm/vs/basic-languages/_.contribution';

// Every Monarch language Monaco ships, and nothing else. Alphabetical, so a Monaco upgrade that adds
// one is a one-line diff in the right place. Each of these registers an id, its extensions and a
// loader; none of them loads a grammar until a model asks for it.
import 'monaco-editor/esm/vs/basic-languages/abap/abap.contribution';
import 'monaco-editor/esm/vs/basic-languages/apex/apex.contribution';
import 'monaco-editor/esm/vs/basic-languages/azcli/azcli.contribution';
import 'monaco-editor/esm/vs/basic-languages/bat/bat.contribution';
import 'monaco-editor/esm/vs/basic-languages/bicep/bicep.contribution';
import 'monaco-editor/esm/vs/basic-languages/cameligo/cameligo.contribution';
import 'monaco-editor/esm/vs/basic-languages/clojure/clojure.contribution';
import 'monaco-editor/esm/vs/basic-languages/coffee/coffee.contribution';
import 'monaco-editor/esm/vs/basic-languages/cpp/cpp.contribution';
import 'monaco-editor/esm/vs/basic-languages/csharp/csharp.contribution';
import 'monaco-editor/esm/vs/basic-languages/csp/csp.contribution';
import 'monaco-editor/esm/vs/basic-languages/css/css.contribution';
import 'monaco-editor/esm/vs/basic-languages/cypher/cypher.contribution';
import 'monaco-editor/esm/vs/basic-languages/dart/dart.contribution';
import 'monaco-editor/esm/vs/basic-languages/dockerfile/dockerfile.contribution';
import 'monaco-editor/esm/vs/basic-languages/ecl/ecl.contribution';
import 'monaco-editor/esm/vs/basic-languages/elixir/elixir.contribution';
import 'monaco-editor/esm/vs/basic-languages/flow9/flow9.contribution';
import 'monaco-editor/esm/vs/basic-languages/freemarker2/freemarker2.contribution';
import 'monaco-editor/esm/vs/basic-languages/fsharp/fsharp.contribution';
import 'monaco-editor/esm/vs/basic-languages/go/go.contribution';
import 'monaco-editor/esm/vs/basic-languages/graphql/graphql.contribution';
import 'monaco-editor/esm/vs/basic-languages/handlebars/handlebars.contribution';
import 'monaco-editor/esm/vs/basic-languages/hcl/hcl.contribution';
import 'monaco-editor/esm/vs/basic-languages/html/html.contribution';
import 'monaco-editor/esm/vs/basic-languages/ini/ini.contribution';
import 'monaco-editor/esm/vs/basic-languages/java/java.contribution';
import 'monaco-editor/esm/vs/basic-languages/javascript/javascript.contribution';
import 'monaco-editor/esm/vs/basic-languages/julia/julia.contribution';
import 'monaco-editor/esm/vs/basic-languages/kotlin/kotlin.contribution';
import 'monaco-editor/esm/vs/basic-languages/less/less.contribution';
import 'monaco-editor/esm/vs/basic-languages/lexon/lexon.contribution';
import 'monaco-editor/esm/vs/basic-languages/liquid/liquid.contribution';
import 'monaco-editor/esm/vs/basic-languages/lua/lua.contribution';
import 'monaco-editor/esm/vs/basic-languages/m3/m3.contribution';
import 'monaco-editor/esm/vs/basic-languages/markdown/markdown.contribution';
import 'monaco-editor/esm/vs/basic-languages/mdx/mdx.contribution';
import 'monaco-editor/esm/vs/basic-languages/mips/mips.contribution';
import 'monaco-editor/esm/vs/basic-languages/msdax/msdax.contribution';
import 'monaco-editor/esm/vs/basic-languages/mysql/mysql.contribution';
import 'monaco-editor/esm/vs/basic-languages/objective-c/objective-c.contribution';
import 'monaco-editor/esm/vs/basic-languages/pascal/pascal.contribution';
import 'monaco-editor/esm/vs/basic-languages/pascaligo/pascaligo.contribution';
import 'monaco-editor/esm/vs/basic-languages/perl/perl.contribution';
import 'monaco-editor/esm/vs/basic-languages/pgsql/pgsql.contribution';
import 'monaco-editor/esm/vs/basic-languages/php/php.contribution';
import 'monaco-editor/esm/vs/basic-languages/pla/pla.contribution';
import 'monaco-editor/esm/vs/basic-languages/postiats/postiats.contribution';
import 'monaco-editor/esm/vs/basic-languages/powerquery/powerquery.contribution';
import 'monaco-editor/esm/vs/basic-languages/powershell/powershell.contribution';
import 'monaco-editor/esm/vs/basic-languages/protobuf/protobuf.contribution';
import 'monaco-editor/esm/vs/basic-languages/pug/pug.contribution';
import 'monaco-editor/esm/vs/basic-languages/python/python.contribution';
import 'monaco-editor/esm/vs/basic-languages/qsharp/qsharp.contribution';
import 'monaco-editor/esm/vs/basic-languages/r/r.contribution';
import 'monaco-editor/esm/vs/basic-languages/razor/razor.contribution';
import 'monaco-editor/esm/vs/basic-languages/redis/redis.contribution';
import 'monaco-editor/esm/vs/basic-languages/redshift/redshift.contribution';
import 'monaco-editor/esm/vs/basic-languages/restructuredtext/restructuredtext.contribution';
import 'monaco-editor/esm/vs/basic-languages/ruby/ruby.contribution';
import 'monaco-editor/esm/vs/basic-languages/rust/rust.contribution';
import 'monaco-editor/esm/vs/basic-languages/sb/sb.contribution';
import 'monaco-editor/esm/vs/basic-languages/scala/scala.contribution';
import 'monaco-editor/esm/vs/basic-languages/scheme/scheme.contribution';
import 'monaco-editor/esm/vs/basic-languages/scss/scss.contribution';
import 'monaco-editor/esm/vs/basic-languages/shell/shell.contribution';
import 'monaco-editor/esm/vs/basic-languages/solidity/solidity.contribution';
import 'monaco-editor/esm/vs/basic-languages/sophia/sophia.contribution';
import 'monaco-editor/esm/vs/basic-languages/sparql/sparql.contribution';
import 'monaco-editor/esm/vs/basic-languages/sql/sql.contribution';
import 'monaco-editor/esm/vs/basic-languages/st/st.contribution';
import 'monaco-editor/esm/vs/basic-languages/swift/swift.contribution';
import 'monaco-editor/esm/vs/basic-languages/systemverilog/systemverilog.contribution';
import 'monaco-editor/esm/vs/basic-languages/tcl/tcl.contribution';
import 'monaco-editor/esm/vs/basic-languages/twig/twig.contribution';
import 'monaco-editor/esm/vs/basic-languages/typescript/typescript.contribution';
import 'monaco-editor/esm/vs/basic-languages/typespec/typespec.contribution';
import 'monaco-editor/esm/vs/basic-languages/vb/vb.contribution';
import 'monaco-editor/esm/vs/basic-languages/wgsl/wgsl.contribution';
import 'monaco-editor/esm/vs/basic-languages/xml/xml.contribution';
import 'monaco-editor/esm/vs/basic-languages/yaml/yaml.contribution';

// Vite's `?worker` builds this as its own classic bundle and hands back a constructor. There is no
// `getWorkerUrl` and no blob bootstrap: that trick exists to work around bundlers that cannot emit a
// worker chunk, and reaching for it here would be the one thing that made the CSP argue back.
import EditorWorker from 'monaco-editor/esm/vs/editor/editor.worker?worker';

declare global {
  interface Window {
    MonacoEnvironment?: { getWorker(workerId: string, label: string): Worker };
  }
}

/**
 * One worker for every label, because there is only one label. Nothing above imports a language
 * service, so Monaco never asks for a `json` or `typescript` worker — and if a future change makes it
 * ask, handing back the diff worker would fail loudly in the console rather than quietly return
 * wrong answers.
 */
window.MonacoEnvironment = { getWorker: () => new EditorWorker() };

export { monaco };
export type MonacoApi = typeof monaco;
