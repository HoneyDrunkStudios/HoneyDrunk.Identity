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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('reports', type=Path, nargs='+')
    args = parser.parse_args()
    convert(args.reports, args.output, args.root)


if __name__ == '__main__':
    main()
