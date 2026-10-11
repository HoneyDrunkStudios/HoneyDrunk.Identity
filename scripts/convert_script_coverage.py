"""Convert executed Python/Pester Cobertura line and branch counts to Sonar XML."""
import argparse
from pathlib import Path
import re
import xml.etree.ElementTree as ET


def source_path(filename, sources, root):
    candidates = {(source / filename).resolve() for source in sources}
    matches = [path for path in candidates if path.is_relative_to(root) and path.is_file()]
    if len(matches) != 1:
        raise ValueError('Coverage source must resolve to one existing file inside the repository')
    return matches[0].relative_to(root).as_posix()


def line_attributes(line):
    number, hits = int(line.attrib['number']), int(line.attrib['hits'])
    if number < 1 or hits < 0:
        raise ValueError('Invalid measured line number or hit count')
    attributes = {'lineNumber': str(number), 'covered': str(hits > 0).lower()}
    if line.get('branch') == 'true':
        counts = re.fullmatch(r'[\d.]+% \((\d+)/(\d+)\)', line.get('condition-coverage', ''), re.ASCII)
        if not counts:
            raise ValueError('Branch coverage must include measured covered/total counts')
        covered, total = map(int, counts.groups())
        if covered > total or total < 1:
            raise ValueError('Invalid measured branch counts')
        attributes.update(branchesToCover=str(total), coveredBranches=str(covered))
    return attributes


def add_report(report, root, files):
    document = ET.parse(report).getroot()
    if document.tag != 'coverage':
        raise ValueError('Expected a Cobertura coverage report')
    sources = [root / item.text for item in document.findall('./sources/source') if item.text]
    for item in document.findall('./packages/package/classes/class'):
        name = source_path(item.attrib['filename'], sources or [root], root)
        lines = files.setdefault(name, {})
        # Class-level lines already include method lines. Counting both would
        # duplicate measured commands, especially in Pester reports.
        for line in item.findall('./lines/line'):
            attributes = line_attributes(line)
            number = int(attributes['lineNumber'])
            if number in lines:
                raise ValueError('Overlapping coverage lines require a producer-side merge')
            lines[number] = attributes


def convert(reports, output, root):
    root = root.resolve()
    files = {}
    for report in reports:
        add_report(report, root, files)
    if not files or not all(files.values()):
        raise ValueError('Every coverage source must contain measured executable lines')
    result = ET.Element('coverage', version='1')
    for name, lines in sorted(files.items()):
        file_element = ET.SubElement(result, 'file', path=name)
        for _, attributes in sorted(lines.items()):
            ET.SubElement(file_element, 'lineToCover', attributes)
    output.parent.mkdir(parents=True, exist_ok=True)
    ET.indent(result)
    ET.ElementTree(result).write(output, encoding='utf-8', xml_declaration=True)


def merge_generic(reports, output):
    result = ET.Element('coverage', version='1')
    seen = set()
    for report in reports:
        document = ET.parse(report).getroot()
        if document.tag != 'coverage' or document.get('version') != '1':
            raise ValueError('Expected Sonar generic coverage version 1')
        for item in document:
            path = item.get('path', '')
            if item.tag != 'file' or not path or path in seen:
                raise ValueError('Coverage files must have distinct source paths')
            seen.add(path)
            result.append(item)
    if not seen:
        raise ValueError('No measured coverage files to merge')
    output.parent.mkdir(parents=True, exist_ok=True)
    ET.indent(result)
    ET.ElementTree(result).write(output, encoding='utf-8', xml_declaration=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--generic', action='store_true', help='Merge disjoint already-converted reports')
    parser.add_argument('reports', type=Path, nargs='+')
    args = parser.parse_args()
    if args.generic:
        merge_generic(args.reports, args.output)
    else:
        convert(args.reports, args.output, args.root)


if __name__ == '__main__':
    main()
