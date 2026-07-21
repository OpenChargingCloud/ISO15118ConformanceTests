import com.siemens.ct.exi.core.EXIFactory;
import com.siemens.ct.exi.core.FidelityOptions;
import com.siemens.ct.exi.core.exceptions.EXIException;
import com.siemens.ct.exi.core.grammars.Grammars;
import com.siemens.ct.exi.core.helpers.DefaultEXIFactory;
import com.siemens.ct.exi.grammars.GrammarFactory;
import com.siemens.ct.exi.main.api.sax.EXIResult;
import com.siemens.ct.exi.main.api.sax.EXISource;

import org.xml.sax.InputSource;
import org.xml.sax.XMLReader;

import javax.xml.parsers.SAXParserFactory;
import javax.xml.transform.OutputKeys;
import javax.xml.transform.Transformer;
import javax.xml.transform.TransformerFactory;
import javax.xml.transform.sax.SAXSource;
import javax.xml.transform.stream.StreamResult;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;

/**
 * Independent second EXI oracle (Siemens EXIficient, a generic W3C-EXI-spec processor) used to
 * cross-validate the XMLDSig fragment wire encoding produced by our generator and already
 * byte-diffed against cbV2G. Encodes/decodes schema-informed EXI (bit-packed, non-strict fidelity
 * — the same convention cbV2G/cbexigen and our generator use) against an XSD entry point that
 * pulls in the rest of a message set's schema via its own {@code <xs:import>} chain.
 *
 * Development tool only — not part of `dotnet test`. See README.md.
 */
public class ExificientRef {

    public static void main(String[] args) throws Exception {
        if (args.length < 5) {
            usage();
            System.exit(2);
        }

        String mode = args[0];
        switch (mode) {
            case "encode":
                encode(args[1], "fragment".equals(args[2]), args[3], args[4]);
                break;
            case "decode":
                decode(args[1], "fragment".equals(args[2]), args[3], args[4]);
                break;
            default:
                usage();
                System.exit(2);
        }
    }

    private static void usage() {
        System.err.println("usage: ExificientRef encode <xsd-entry-point> <fragment|document> <in.xml> <out.hex>");
        System.err.println("       ExificientRef decode <xsd-entry-point> <fragment|document> <in.hex> <out.xml>");
    }

    private static EXIFactory buildFactory(String xsdPath, boolean fragment) throws EXIException {
        Grammars grammars = GrammarFactory.newInstance().createGrammars(xsdPath);
        EXIFactory ef = DefaultEXIFactory.newInstance();
        ef.setGrammars(grammars);
        ef.setFidelityOptions(FidelityOptions.createDefault());
        ef.setFragment(fragment);
        if (System.getenv("EXIF_CANONICAL") != null) {
            try {
                ef.getEncodingOptions().setOption(com.siemens.ct.exi.core.EncodingOptions.CANONICAL_EXI);
            } catch (Exception e) { System.err.println("no canonical option: " + e); }
        }
        return ef;
    }

    private static void encode(String xsdPath, boolean fragment, String inXml, String outHex) throws Exception {
        EXIFactory ef = buildFactory(xsdPath, fragment);

        SAXParserFactory spf = SAXParserFactory.newInstance();
        spf.setNamespaceAware(true);
        XMLReader xmlReader = spf.newSAXParser().getXMLReader();

        ByteArrayOutputStream bos = new ByteArrayOutputStream();
        EXIResult exiResult = new EXIResult(ef);
        exiResult.setOutputStream(bos);

        try (FileInputStream in = new FileInputStream(inXml)) {
            SAXSource source = new SAXSource(xmlReader, new InputSource(in));
            TransformerFactory.newInstance().newTransformer().transform(source, exiResult);
        }

        byte[] bytes = bos.toByteArray();
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < bytes.length; i++) {
            if (i > 0) sb.append(' ');
            sb.append(String.format("%02x", bytes[i] & 0xFF));
        }
        Files.write(Paths.get(outHex), sb.toString().getBytes(StandardCharsets.US_ASCII));
        System.out.println(sb);
    }

    private static void decode(String xsdPath, boolean fragment, String inHex, String outXml) throws Exception {
        EXIFactory ef = buildFactory(xsdPath, fragment);

        String hex = new String(Files.readAllBytes(Paths.get(inHex)), StandardCharsets.US_ASCII)
                .replaceAll("\\s+", "");
        byte[] bytes = new byte[hex.length() / 2];
        for (int i = 0; i < bytes.length; i++) {
            bytes[i] = (byte) Integer.parseInt(hex.substring(i * 2, i * 2 + 2), 16);
        }

        EXISource exiSource = new EXISource(ef);
        SAXSource saxSource = new SAXSource(exiSource.getXMLReader(), new InputSource(new ByteArrayInputStream(bytes)));

        Transformer transformer = TransformerFactory.newInstance().newTransformer();
        transformer.setOutputProperty(OutputKeys.OMIT_XML_DECLARATION, "yes");
        transformer.setOutputProperty(OutputKeys.INDENT, "yes");
        try (OutputStream os = new FileOutputStream(outXml)) {
            transformer.transform(saxSource, new StreamResult(os));
        }
        System.out.println(new String(Files.readAllBytes(Paths.get(outXml)), StandardCharsets.UTF_8));
    }
}
