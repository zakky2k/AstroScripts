#!/usr/bin/env python3
"""Recover verified PixInsight Float32 monochrome parallel swaps to XISF 1.0.

Also supports --repair-header on XISF files made by the earlier converter.
Source swap data is never modified. No external Python dependencies.
"""
import argparse
from pathlib import Path
import re
import struct
import xml.etree.ElementTree as ET

SWAP_HEADER_SIZE = 128
ATTACHMENT_OFFSET = 4096
CHUNK = 4 * 1024 * 1024
NS = 'http://www.pixinsight.com/xisf'


def create_xml(width, height, size, image_id='RecoveredImage'):
    xml = ('<?xml version="1.0" encoding="UTF-8"?>\n'
           '<xisf xmlns="http://www.pixinsight.com/xisf" version="1.0">\n'
           f'  <Image id="{image_id}" geometry="{width}:{height}:1" '
           'sampleFormat="Float32" bounds="0:1" colorSpace="Gray" '
           'pixelStorage="Planar" byteOrder="little" '
           f'location="attachment:{ATTACHMENT_OFFSET}:{size}"/>\n'
           '  <Metadata/>\n'
           '</xisf>\n')
    return xml.encode('utf-8')


def validate(path):
    """Structural XISF checks; does not replace testing with PixInsight."""
    path = Path(path)
    with path.open('rb') as f:
        preamble = f.read(16)
        if len(preamble) != 16 or preamble[:8] != b'XISF0100':
            raise ValueError('Invalid XISF signature')
        n, reserved = struct.unpack_from('<II', preamble, 8)
        if reserved != 0 or n <= 0 or n > ATTACHMENT_OFFSET - 16:
            raise ValueError('Invalid XISF header length/reserved field')
        xml = f.read(n)
    root = ET.fromstring(xml)
    if root.tag != '{%s}xisf' % NS or root.get('version') != '1.0':
        raise ValueError('Invalid XISF namespace/version')
    images = root.findall('{%s}Image' % NS)
    if len(images) != 1 or root.find('{%s}Metadata' % NS) is None:
        raise ValueError('Expected one image and required Metadata element')
    im = images[0]
    for key in ('geometry', 'sampleFormat', 'bounds', 'colorSpace', 'location'):
        if key not in im.attrib:
            raise ValueError('Missing mandatory Image attribute: ' + key)
    if im.get('sampleFormat') != 'Float32' or im.get('colorSpace') != 'Gray':
        raise ValueError('Unsupported image type')
    dims = tuple(int(x) for x in im.get('geometry').split(':'))
    if len(dims) != 3 or dims[2] != 1 or min(dims) <= 0:
        raise ValueError('Invalid image geometry')
    bounds = tuple(float(x) for x in im.get('bounds').split(':'))
    if len(bounds) != 2 or bounds[0] >= bounds[1]:
        raise ValueError('Invalid bounds')
    loc = im.get('location').split(':')
    if len(loc) != 3 or loc[0] != 'attachment':
        raise ValueError('Invalid image location')
    offset, size = int(loc[1]), int(loc[2])
    if offset < 16 + n or size != dims[0] * dims[1] * 4:
        raise ValueError('Invalid attachment offset or size')
    if path.stat().st_size != offset + size:
        raise ValueError('Incorrect total XISF file size')
    print(f'Validated: {dims[0]}x{dims[1]}, Float32 Gray, {size:,} image bytes')


def repair_header(path):
    """Upgrade our earlier XISF file header in place without touching pixels."""
    path = Path(path)
    with path.open('r+b') as f:
        preamble = f.read(16)
        if preamble[:8] != b'XISF0100':
            raise ValueError('Not an XISF file')
        n, reserved = struct.unpack_from('<II', preamble, 8)
        if reserved or n <= 0 or n > 4080:
            raise ValueError('Unsupported preamble')
        root = ET.fromstring(f.read(n))
        if root.tag != '{%s}xisf' % NS or root.get('version') != '1.0':
            raise ValueError('Unexpected XML root')
        images = root.findall('{%s}Image' % NS)
        if len(images) != 1:
            raise ValueError('Expected one image')
        im = images[0]
        if im.get('sampleFormat') != 'Float32':
            raise ValueError('Only Float32 is supported')
        if 'bounds' not in im.attrib:
            im.set('bounds', '0:1')
        if root.find('{%s}Metadata' % NS) is None:
            ET.SubElement(root, '{%s}Metadata' % NS)
        xml = ET.tostring(root, encoding='utf-8', xml_declaration=True)
        if 16 + len(xml) > ATTACHMENT_OFFSET:
            raise ValueError('Header does not fit before the image data')
        loc = im.get('location', '').split(':')
        if len(loc) != 3 or loc[0] != 'attachment' or int(loc[1]) != ATTACHMENT_OFFSET:
            raise ValueError('Unexpected attachment offset: cannot patch in place')
        if path.stat().st_size != ATTACHMENT_OFFSET + int(loc[2]):
            raise ValueError('File size does not match image attachment')
        f.seek(8)
        f.write(struct.pack('<I', len(xml)))
        f.seek(16)
        f.write(xml)
        f.write(b'\0' * (ATTACHMENT_OFFSET - 16 - len(xml)))
    validate(path)


def recover(primary, output):
    primary, output = Path(primary).resolve(), Path(output).resolve()
    if not primary.is_file() or primary.suffix.lower() != '.swp' or re.search(r'-part\d+\.swp$', primary.name, re.I):
        raise ValueError('Specify an existing primary .swp, not a continuation')
    with primary.open('rb') as f:
        head = f.read(SWAP_HEADER_SIZE)
    if not head.startswith(b'egamiwar') or head[0x0c:0x14] != bytes.fromhex('0101002001000000'):
        raise ValueError('Unrecognized/unsupported PixInsight swap format')
    width, height, channels = struct.unpack_from('<III', head, 0x1c)
    count = struct.unpack_from('<Q', head, 0x68)[0]
    if not (1 <= width <= 200000 and 1 <= height <= 200000 and channels == 1 and 1 <= count <= 256):
        raise ValueError('Unsupported image dimensions/channels/thread count')
    parts = [primary] + [primary.with_name(f'{primary.stem}-part{i:03d}.swp') for i in range(1, count)]
    if any(not p.is_file() for p in parts):
        raise ValueError('Missing one or more swap continuation files')
    expected = width * height * 4
    actual = sum(p.stat().st_size - (SWAP_HEADER_SIZE if i == 0 else 0) for i, p in enumerate(parts))
    if actual != expected:
        raise ValueError(f'Incorrect pixel data size: {actual:,} vs {expected:,}')