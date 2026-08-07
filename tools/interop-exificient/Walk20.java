/*
 * Walk20 — decode one frame event by event and say where the stream stops making sense.
 *
 * `Roundtrip20` answers ok / mismatch / decode-fail. For a decode-fail that is not enough: the
 * message is "Premature EOS found while reading data", which only says EXIficient ran out of bits
 * before its grammar was satisfied. It does not say *where*. A frame can fail that way because the
 * very first event code was wrong and everything after it was garbage, or because 240 of 241 bytes
 * were read correctly and one trailing particle was missing — and the two need completely different
 * fixes.
 *
 * So this drives EXIficient's event API directly instead of its SAX bridge, prints every event as it
 * is decoded together with the number of stream bytes consumed so far, and then prints the exception.
 * The last line before the failure is the last thing the two codecs agreed about.
 *
 * Written for `AC_ChargeParameterDiscoveryRes_DER` (see docs/interop-runs/2026-08-07-exificient-iso20/),
 * the one frame in the -20 corpus whose expected bytes have no external provenance at all: cbexigen
 * cannot generate the DER schemas, so our own encoder is the only thing that has ever written it.
 *
 *     in :  <name> \t <absolute schema path> \t <hex>
 *     out:  a trace on stdout, one event per line
 *
 * Build:  javac -cp <decoder.jar> -d <outdir> Walk20.java
 * Run:    java -cp <decoder.jar>:<outdir> Walk20 <jobs.tsv>
 */

import com.siemens.ct.exi.core.EXIBodyDecoder;
import com.siemens.ct.exi.core.EXIFactory;
import com.siemens.ct.exi.core.EXIStreamDecoder;
import com.siemens.ct.exi.core.context.QNameContext;
import com.siemens.ct.exi.core.grammars.Grammars;
import com.siemens.ct.exi.core.grammars.event.EventType;
import com.siemens.ct.exi.core.helpers.DefaultEXIFactory;
import com.siemens.ct.exi.grammars.GrammarFactory;

import org.apache.xerces.xni.XMLResourceIdentifier;
import org.apache.xerces.xni.parser.XMLEntityResolver;
import org.apache.xerces.xni.parser.XMLInputSource;

import java.io.ByteArrayInputStream;
import java.io.FilterInputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.List;

public class Walk20 {

    public static void main(String[] args) throws Exception {
        if (args.length != 1) {
            System.err.println("usage: Walk20 <jobs.tsv>");
            System.exit(2);
        }

        for (String job : Files.readAllLines(Paths.get(args[0]), StandardCharsets.UTF_8)) {
            if (job.isBlank()) continue;
            String[] parts = job.split("\t", 3);
            walk(parts[0], parts[1], unhex(parts[2]));
        }
    }

    private static void walk(String name, String schemaPath, byte[] exi) {
        System.out.println("=== " + name + "   " + exi.length + " B");

        Grammars grammars;
        try {
            grammars = GrammarFactory.newInstance().createGrammars(
                    Paths.get(schemaPath).toAbsolutePath().toUri().toString(), OFFLINE);
        } catch (Exception e) {
            System.out.println("    schema: " + e);
            return;
        }

        Counting input = new Counting(new ByteArrayInputStream(exi));
        Deque<String> open = new ArrayDeque<>();
        int events = 0;

        try {
            EXIFactory factory = DefaultEXIFactory.newInstance();
            factory.setGrammars(grammars);

            EXIStreamDecoder stream = factory.createEXIStreamDecoder();
            EXIBodyDecoder decoder = stream.decodeHeader(input);

            EventType event;
            while ((event = decoder.next()) != null) {
                events++;
                // Read the position *before* decoding: the count afterwards includes the byte this
                // event's own value came out of, which makes every line look one byte late.
                int at = input.count;
                String line;

                switch (event) {
                    case START_DOCUMENT:
                        decoder.decodeStartDocument();
                        line = "SD";
                        break;

                    // One decode method serves every variant of a kind; the variant itself is worth
                    // printing, because a *_GENERIC_UNDECLARED where a declared event was expected is
                    // already the divergence.
                    case START_ELEMENT:
                    case START_ELEMENT_NS:
                    case START_ELEMENT_GENERIC:
                    case START_ELEMENT_GENERIC_UNDECLARED: {
                        QNameContext qname = decoder.decodeStartElement();
                        line = "SE(" + qname.getLocalName() + ")" + variant(event, EventType.START_ELEMENT);
                        open.push(qname.getLocalName());
                        break;
                    }

                    case END_ELEMENT:
                    case END_ELEMENT_UNDECLARED: {
                        QNameContext qname = decoder.decodeEndElement();
                        line = "EE(" + qname.getLocalName() + ")" + variant(event, EventType.END_ELEMENT);
                        if (!open.isEmpty()) open.pop();
                        break;
                    }

                    case CHARACTERS:
                    case CHARACTERS_GENERIC:
                    case CHARACTERS_GENERIC_UNDECLARED:
                        line = "CH \"" + decoder.decodeCharacters() + "\""
                               + variant(event, EventType.CHARACTERS);
                        break;

                    case ATTRIBUTE:
                    case ATTRIBUTE_NS:
                    case ATTRIBUTE_GENERIC:
                    case ATTRIBUTE_GENERIC_UNDECLARED:
                    case ATTRIBUTE_INVALID_VALUE:
                    case ATTRIBUTE_ANY_INVALID_VALUE: {
                        QNameContext qname = decoder.decodeAttribute();
                        line = "AT(" + qname.getLocalName() + ") = \"" + decoder.getAttributeValue() + "\""
                               + variant(event, EventType.ATTRIBUTE);
                        break;
                    }

                    case ATTRIBUTE_XSI_TYPE:
                        line = "AT(xsi:type) -> " + decoder.decodeAttributeXsiType().getLocalName();
                        break;

                    case ATTRIBUTE_XSI_NIL:
                        decoder.decodeAttributeXsiNil();
                        line = "AT(xsi:nil)";
                        break;

                    case NAMESPACE_DECLARATION:
                        line = "NS " + decoder.decodeNamespaceDeclaration().namespaceURI;
                        break;

                    case END_DOCUMENT:
                        decoder.decodeEndDocument();
                        System.out.printf("  %4d  %s%s%n", at, indent(open.size()), "ED");
                        System.out.println("    read to completion: " + events + " events, "
                                           + input.count + " of " + exi.length + " bytes");
                        return;

                    default:
                        System.out.printf("  %4d  %s%s%n", at, indent(open.size()),
                                          "unhandled event " + event + " — stopping");
                        return;
                }

                System.out.printf("  %4d  %s%s%n", at, indent(open.size()), line);
            }

            System.out.println("    next() returned null after " + events + " events");

        } catch (Exception e) {
            System.out.println("    FAILED after " + events + " events, "
                               + input.count + " of " + exi.length + " bytes consumed");
            System.out.println("    open: " + String.join(" / ", reversed(open)));
            System.out.println("    " + e.getClass().getName()
                               + (e.getMessage() == null ? "" : ": " + e.getMessage()));
        }
    }

    private static String indent(int depth) {
        return "  ".repeat(Math.max(0, depth));
    }

    /** Names the event only when it is not the plain, schema-declared one. */
    private static String variant(EventType actual, EventType plain) {
        return actual == plain ? "" : "   [" + actual + "]";
    }

    private static List<String> reversed(Deque<String> stack) {
        java.util.ArrayList<String> out = new java.util.ArrayList<>(stack);
        java.util.Collections.reverse(out);
        return out;
    }

    /** Bytes pulled out of the stream so far — enough to say where in the frame an event landed. */
    private static final class Counting extends FilterInputStream {
        int count;
        Counting(InputStream in) { super(in); }
        @Override public int read() throws java.io.IOException {
            int b = super.read();
            if (b >= 0) count++;
            return b;
        }
        @Override public int read(byte[] b, int off, int len) throws java.io.IOException {
            int n = super.read(b, off, len);
            if (n > 0) count += n;
            return n;
        }
    }

    /** Same reason as in Roundtrip20: a grammar build must never touch the network. */
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

    private static byte[] unhex(String hex) {
        String clean = hex.replace(" ", "");
        byte[] bytes = new byte[clean.length() / 2];
        for (int i = 0; i < bytes.length; i++)
            bytes[i] = (byte) Integer.parseInt(clean.substring(i * 2, i * 2 + 2), 16);
        return bytes;
    }
}
