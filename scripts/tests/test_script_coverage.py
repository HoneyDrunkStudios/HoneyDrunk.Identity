"""Preserve executed counts; never manufacture coverage for missing sources."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('converter', Path(__file__).parents[1] / 'convert_script_coverage.py')
converter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(converter)


class ScriptCoverageTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name).resolve()
        (self.root / 'scripts').mkdir()
        (self.root / 'scripts/example.py').write_text('first\nsecond\nthird\n', encoding='utf-8')
        self.output = self.root / 'results/sonar.xml'

    def report(self, lines, filename='example.py', source=None, name='coverage.xml'):
        document = ET.Element('coverage')
        sources = ET.SubElement(document, 'sources')
        ET.SubElement(sources, 'source').text = str(source or self.root / 'scripts')
        classes = ET.SubElement(ET.SubElement(ET.SubElement(document, 'packages'), 'package'), 'classes')
        item = ET.SubElement(classes, 'class', filename=filename)
        parent = ET.SubElement(item, 'lines')
        for attributes in lines:
            ET.SubElement(parent, 'line', attributes)
        # Duplicate method-level lines must not be imported a second time.
        method = ET.SubElement(ET.SubElement(item, 'methods'), 'method')
        ET.SubElement(ET.SubElement(method, 'lines'), 'line', number='1', hits='999')
        path = self.root / name
        ET.ElementTree(document).write(path)
        return path

    def test_real_counts_paths_and_cli_are_preserved(self):
        report = self.report([{'number': '2', 'hits': '0'},
                              {'number': '1', 'hits': '3', 'branch': 'true', 'condition-coverage': '50% (1/2)'}])
        with patch('sys.argv', ['convert', '--root', str(self.root), '--output', str(self.output), str(report)]):
            converter.main()
        document = ET.parse(self.output).getroot()
        self.assertEqual(document.attrib, {'version': '1'})
        self.assertEqual(document[0].attrib, {'path': 'scripts/example.py'})
        self.assertEqual([line.attrib for line in document[0]], [
            {'lineNumber': '1', 'covered': 'true', 'branchesToCover': '2', 'coveredBranches': '1'},
            {'lineNumber': '2', 'covered': 'false'}])

    def test_outside_missing_or_ambiguous_sources_are_rejected(self):
        for filename in ('../missing.py', str(self.root.parent / 'outside.py')):
            report = self.report([{'number': '1', 'hits': '1'}], filename)
            with self.subTest(filename=filename), self.assertRaises(ValueError):
                converter.convert([report], self.output, self.root)
        (self.root / 'example.py').write_text('other\n', encoding='utf-8')
        with self.assertRaises(ValueError):
            converter.source_path('example.py', [self.root, self.root / 'scripts'], self.root)
        self.assertFalse(self.output.exists())

    def test_no_reports_or_empty_or_wrong_reports_fail_closed(self):
        for reports in ([], [self.report([])]):
            with self.assertRaises(ValueError):
                converter.convert(reports, self.output, self.root)
        invalid = self.root / 'invalid.xml'
        invalid.write_text('<not-coverage/>', encoding='utf-8')
        with self.assertRaises(ValueError):
            converter.convert([invalid], self.output, self.root)
        with self.assertRaises(FileNotFoundError):
            converter.convert([self.root / 'missing.xml'], self.output, self.root)

    def test_invalid_line_or_branch_counts_are_rejected(self):
        cases = [{'number': '0', 'hits': '1'}, {'number': '1', 'hits': '-1'},
                 {'number': '1', 'hits': '1', 'branch': 'true', 'condition-coverage': '50%'},
                 {'number': '1', 'hits': '1', 'branch': 'true', 'condition-coverage': '200% (2/1)'},
                 {'number': '1', 'hits': '1', 'branch': 'true', 'condition-coverage': '0% (0/0)'}]
        for attributes in cases:
            line = ET.Element('line', attributes)
            with self.subTest(attributes=attributes), self.assertRaises(ValueError):
                converter.line_attributes(line)

    def test_overlapping_producer_lines_cannot_inflate_coverage(self):
        report = self.report([{'number': '1', 'hits': '1'}])
        with self.assertRaises(ValueError):
            converter.convert([report, report], self.output, self.root)


if __name__ == '__main__':
    unittest.main()
