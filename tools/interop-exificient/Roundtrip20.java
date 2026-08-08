/*
 * Roundtrip20 — one JVM, one grammar per schema, every frame.
 *
 * The CLI (`com.siemens.ct.exi.main.cmd.EXIficientCMD`) rebuilds the schema model on every
 * invocation, so a corpus run meant ~700 JVMs each parsing the same XSDs again. On this rig that
 * construction fails intermittently — Xerces reporting it cannot read an import that is present and
 * readable — often enough that a full run never finished. See README.md for what was excluded.
 *
 * This does the obvious thing instead: build each Grammars once, cache it, and reuse it for every
 * frame. Eight schema loads instead of six hundred and ninety-four, each retried a few times, and
 * after that not a single further schema read for the rest of the run.
 *
 * Protocol is tab-separated lines rather than JSON, because the jar carries no JSON library and the
 * Python side has one:
 *
 *     in :  <name> \t <absolute schema path> \t <hex>
 *     out:  <name> \t <ok|mismatch|decode-fail|encode-fail> \t <hex or -> \t <detail or ->
 *
 * If the third field starts with '<' it is XML, and the job is encode-only: the result is EXIficient's
 * bytes for that document, verdict `encoded`. That is how a frame our codec writes and theirs cannot
 * read gets pinned down — hand them the message we meant and compare what they produce.
 *
 * If it starts with '?' the rest is hex and the job is decode-only: the result is the XML EXIficient
 * read out of it, on one line, verdict `decoded`. The complement of the above — show me the message
 * they think we sent. Used by `valuepartition20.py` to get at the document a mismatching frame
 * contains, so the repeated string in it can be substituted and the difference attributed.
 *
 * Build:  javac -cp <decoder.jar> -d <outdir> Roundtrip20.java
 * Run:    java -cp <decoder.jar>:<outdir> Roundtrip20 <jobs.tsv> <results.tsv>
 */

import com.siemens.ct.exi.core.EXIFactory;
import com.siemens.ct.exi.core.grammars.Grammars;
import com.siemens.ct.exi.core.helpers.DefaultEXIFactory;
import com.siemens.ct.exi.grammars.GrammarFactory;
import com.siemens.ct.exi.main.api.sax.EXIResult;
import com.siemens.ct.exi.main.api.sax.EXISource;

import org.apache.xerces.xni.XMLResourceIdentifier;
import org.apache.xerces.xni.parser.XMLEntityResolver;
import org.apache.xerces.xni.parser.XMLInputSource;

import org.xml.sax.InputSource;
import org.xml.sax.XMLReader;

import javax.xml.parsers.SAXParserFactory;
import javax.xml.transform.Transformer;
import javax.xml.transform.TransformerFactory;
import javax.xml.transform.sax.SAXSource;
import javax.xml.transform.stream.StreamResult;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

public class Roundtrip20 {

    /** Schema construction is the one step this rig makes unreliable; it now happens once per set. */
    private static final int GRAMMAR_ATTEMPTS = 8;

    private static final Map<String, Grammars> CACHE = new HashMap<>();

    public static void main(String[] args) throws Exception {
        if (args.length != 2) {
            System.err.println("usage: Roundtrip20 <jobs.tsv> <results.tsv>");
            System.exit(2);
        }

        List<String> jobs = Files.readAllLines(Paths.get(args[0]), StandardCharsets.UTF_8);
        int schemaLoads = 0, schemaRetries = 0;

        try (PrintWriter out = new PrintWriter(Files.newBufferedWriter(
                Paths.get(args[1]), StandardCharsets.UTF_8))) {

            for (String job : jobs) {
                if (job.isBlank()) continue;
                String[] parts = job.split("\t", 3);
                String name = parts[0], schemaPath = parts[1], hex = parts[2];

                Grammars grammars;
                try {
                    if (!CACHE.containsKey(schemaPath)) {
                        int[] attempts = new int[1];
                        CACHE.put(schemaPath, loadGrammars(schemaPath, attempts));
                        schemaLoads++;
                        schemaRetries += attempts[0];
                    }
                    grammars = CACHE.get(schemaPath);
                } catch (Exception e) {
                    emit(out, name, "decode-fail", null, "schema: " + brief(e));
                    continue;
                }

                if (hex.startsWith("<")) {          // encode-only probe, see the header comment
                    try {
                        emit(out, name, "encoded", hex(encode(hex, grammars)), null);
                    } catch (Exception e) {
                        emit(out, name, "encode-fail", null, brief(e));
                    }
                    continue;
                }

                if (hex.startsWith("?")) {          // decode-only probe, see the header comment
                    try {
                        emit(out, name, "decoded", decode(unhex(hex.substring(1)), grammars), null);
                    } catch (Exception e) {
                        emit(out, name, "decode-fail", null, brief(e));
                    }
                    continue;
                }

                byte[] ours = unhex(hex);

                String xml;
                try {
                    xml = decode(ours, grammars);
                } catch (Exception e) {
                    emit(out, name, "decode-fail", null, brief(e));
                    continue;
                }

                byte[] theirs;
                try {
                    theirs = encode(xml, grammars);
                } catch (Exception e) {
                    emit(out, name, "encode-fail", null, brief(e));
                    continue;
                }

                if (java.util.Arrays.equals(ours, theirs)) {
                    emit(out, name, "ok", null, null);
                } else {
                    emit(out, name, "mismatch", hex(theirs),
                         "ours " + ours.length + " B, theirs " + theirs.length + " B");
                }
            }
        }

        System.err.println("schema models built: " + schemaLoads + " (retries: " + schemaRetries + ")");
    }

    /**
     * Resolves every remote entity to nothing, so building a schema model never touches the network.
     *
     * <p>This is the whole bug, and it took an afternoon. `xmldsig-core-schema.xsd` — W3C's, pulled in
     * by ISO's `V2G_CI_CommonTypes.xsd` and therefore by every -20 message set — opens with
     *
     * <pre>&lt;!DOCTYPE schema PUBLIC "-//W3C//DTD XMLSchema 200102//EN"
     *                        "http://www.w3.org/2001/XMLSchema.dtd"&gt;</pre>
     *
     * so Xerces fetches that DTD from w3.org every single time a grammar is built. W3C has rate-limited
     * those fetches for years. Under a corpus run that is hundreds of requests in a few minutes, and
     * once they start being refused the failure surfaces as
     * `Failed to read schema document 'xmldsig-core-schema.xsd'` — naming the local file, which is
     * present and readable, rather than the remote DTD that actually failed.
     *
     * <p>It explains everything that looked inexplicable: the non-determinism (it is a network), the
     * way it worsened across a session (throttling), that a fresh distro did not help, that no JVM
     * version helped, and above all that the *only* schema which never failed is SupportedAppProtocol
     * — the one with no imports, and therefore no DOCTYPE anywhere in its chain.
     *
     * <p>Returning an empty source for the DTD is safe here: its declarations describe the XML Schema
     * language itself and are not needed to build an XSModel. The declarations that matter are in the
     * DOCTYPE's own internal subset, which stays. Local references return {@code null}, which leaves
     * Xerces to resolve them exactly as before.
     */
    private static final XMLEntityResolver OFFLINE = new XMLEntityResolver() {
        @Override
        public XMLInputSource resolveEntity(XMLResourceIdentifier id) {
            String systemId = id.getExpandedSystemId();
            if (systemId != null && (systemId.startsWith("http://") || systemId.startsWith("https://")))
                return new XMLInputSource(id.getPublicId(), systemId, id.getBaseSystemId(),
                                          new java.io.StringReader(""), "UTF-8");
            return null;
        }
    };

    /** Build one schema model, offline. The retry is a leftover guard; it should never fire now. */
    private static Grammars loadGrammars(String schemaPath, int[] attemptsOut) throws Exception {
        String uri = Paths.get(schemaPath).toAbsolutePath().toUri().toString();
        Exception last = null;
        for (int attempt = 0; attempt < GRAMMAR_ATTEMPTS; attempt++) {
            try {
                Grammars g = GrammarFactory.newInstance().createGrammars(uri, OFFLINE);
                attemptsOut[0] = attempt;
                return g;
            } catch (Exception e) {
                last = e;
            }
        }
        attemptsOut[0] = GRAMMAR_ATTEMPTS;
        throw last;
    }

    private static String decode(byte[] exi, Grammars grammars) throws Exception {
        EXIFactory factory = DefaultEXIFactory.newInstance();
        factory.setGrammars(grammars);

        EXISource source = new EXISource(factory);
        source.setInputSource(new InputSource(new ByteArrayInputStream(exi)));

        ByteArrayOutputStream xml = new ByteArrayOutputStream();
        Transformer transformer = TransformerFactory.newInstance().newTransformer();
        transformer.transform(source, new StreamResult(xml));

        return xml.toString(StandardCharsets.UTF_8);
    }

    private static byte[] encode(String xml, Grammars grammars) throws Exception {
        EXIFactory factory = DefaultEXIFactory.newInstance();
        factory.setGrammars(grammars);

        ByteArrayOutputStream exi = new ByteArrayOutputStream();
        EXIResult result = new EXIResult(factory);
        result.setOutputStream(exi);

        // XMLReaderFactory is gone in modern JDKs; go through SAXParserFactory instead.
        SAXParserFactory saxFactory = SAXParserFactory.newInstance();
        saxFactory.setNamespaceAware(true);
        XMLReader reader = saxFactory.newSAXParser().getXMLReader();
        reader.setContentHandler(result.getHandler());
        reader.parse(new InputSource(new java.io.StringReader(xml)));

        return exi.toByteArray();
    }

    private static void emit(PrintWriter out, String name, String verdict, String theirHex, String detail) {
        out.println(name + "\t" + verdict + "\t"
                    + (theirHex == null ? "-" : theirHex) + "\t"
                    + (detail == null ? "-" : detail.replace('\t', ' ').replace('\n', ' ')));
        out.flush();
    }

    private static String brief(Exception e) {
        String message = e.getMessage();
        String text = e.getClass().getSimpleName() + (message == null ? "" : ": " + message);
        return text.length() > 200 ? text.substring(0, 200) : text;
    }

    private static byte[] unhex(String hex) {
        byte[] bytes = new byte[hex.length() / 2];
        for (int i = 0; i < bytes.length; i++)
            bytes[i] = (byte) Integer.parseInt(hex.substring(i * 2, i * 2 + 2), 16);
        return bytes;
    }

    private static String hex(byte[] bytes) {
        StringBuilder sb = new StringBuilder(bytes.length * 2);
        for (byte b : bytes) sb.append(String.format("%02x", b));
        return sb.toString();
    }
}
