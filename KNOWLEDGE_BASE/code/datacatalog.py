#!/usr/bin/env python3
"""
DATACATALOG.PY — Herramienta de análisis de XSDs DGII
======================================================
Analiza los XSDs de facturación electrónica DGII y genera catálogos
de tipos de datos, elementos y restricciones.

Uso:
    python datacatalog.py                      # Analizar todos los XSDs
    python datacatalog.py --xsd "e-CF 32"     # Analizar un XSD específico
    python datacatalog.py --output json       # Salida JSON
    python datacatalog.py --stats             # Solo estadísticas
"""

import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from collections import defaultdict
import re

XSD_DIR = Path(r"C:\Users\victor\Desktop\Sistema Pos\documentacion xsd")

# Mapeo de tipos complejos de interés
TIPO_INTERES = {
    'EncabezadoECF32',
    'EmisorECF32',
    'CompradorECF32',
    'TotalesECF32',
    'DetallesItemsECF32',
    'ItemECF32',
    'CodigosItemECF32',
    'SubcantidadItemECF32',
    'SubDescuentoItemECF32',
    'SubRecargoItemECF32',
    'ImpuestoAdicionalItemECF32',
    'OtraMonedaDetalleItemECF32',
    'SubtotalesECF32',
    'DescuentosORecargosECF32',
    'DescuentoORecargoECF32',
    'PaginacionECF32',
    'PaginaECF32',
    'InformacionReferenciaECF32',
    'TransporteECF32',
    'InformacionesAdicionalesECF32',
    'ANECF',
    'ACECF',
    'ARECF',
    'RFCE32',
    'RFCEEncabezado',
    'RFCEEmisor',
    'RFCEComprador',
    'RFCETotales',
    'RFCEItems',
    'RFCEItem',
}


def parse_xsd(xsd_path: Path) -> dict:
    """Parsear un XSD y extraer tipos simples, complejos y elementos."""
    result = {
        'path': str(xsd_path),
        'simple_types': [],
        'complex_types': [],
        'elements': [],
        'types': set(),
    }
    
    tree = ET.parse(xsd_path)
    root = tree.getroot()
    
    # Namespace
    ns = {'xs': 'http://www.w3.org/2001/XMLSchema'}
    
    # Tipos simples
    for st in root.findall('.//xs:simpleType', ns):
        name = st.get('name', '')
        restriction = st.find('xs:restriction', ns)
        if restriction is not None:
            base = restriction.get('base', '')
            enumerations = [e.get('value', '') for e in restriction.findall('xs:enumeration', ns)]
            patterns = [p.get('value', '') for p in restriction.findall('xs:pattern', ns)]
            result['simple_types'].append({
                'name': name,
                'base': base,
                'enumerations': enumerations,
                'patterns': patterns,
            })
            result['types'].add(name)
    
    # Tipos complejos
    for ct in root.findall('.//xs:complexType', ns):
        name = ct.get('name', '')
        sequence = ct.find('xs:sequence', ns)
        elements = []
        
        if sequence is not None:
            for elem in sequence.findall('xs:element', ns):
                elem_name = elem.get('name', '')
                elem_type = elem.get('type', '')
                min_occ = elem.get('minOccurs', '1')
                max_occ = elem.get('maxOccurs', '1')
                
                # Si es complejo (tiene sub-secuencia)
                inner_seq = elem.find('xs:complexType/xs:sequence', ns)
                inner_elements = []
                if inner_seq is not None:
                    for ie in inner_seq.findall('xs:element', ns):
                        inner_elements.append({
                            'name': ie.get('name', ''),
                            'type': ie.get('type', ''),
                            'minOccurs': ie.get('minOccurs', '1'),
                            'maxOccurs': ie.get('maxOccurs', '1'),
                        })
                    elements.append({
                        'name': elem_name,
                        'type': elem_type,
                        'minOccurs': min_occ,
                        'maxOccurs': max_occ,
                        'complex': True,
                        'elements': inner_elements,
                    })
                else:
                    elements.append({
                        'name': elem_name,
                        'type': elem_type,
                        'minOccurs': min_occ,
                        'maxOccurs': max_occ,
                        'complex': False,
                    })
        
        result['complex_types'].append({
            'name': name,
            'elements': elements,
        })
        result['types'].add(name)
    
    # Elementos raíz
    for elem in root.findall('.//xs:element', ns):
        if elem.get('name'):
            result['elements'].append({
                'name': elem.get('name'),
                'type': elem.get('type'),
                'minOccurs': elem.get('minOccurs', '1'),
                'maxOccurs': elem.get('maxOccurs', '1'),
            })
    
    return result


def analyze_all():
    """Analizar todos los XSDs y generar resumen."""
    xsd_files = sorted(XSD_DIR.glob("*.xsd"))
    
    print(f"\n{'='*70}")
    print(f"DATACATALOG — Análisis de XSDs DGII Facturación Electrónica")
    print(f"{'='*70}")
    print(f"Directorio: {XSD_DIR}")
    print(f"XSDs encontrados: {len(xsd_files)}")
    print(f"{'='*70}\n")
    
    all_data = {}
    
    for xsd_file in xsd_files:
        print(f"\n📄 {xsd_file.name}")
        print(f"   Tamaño: {xsd_file.stat().st_size:,} bytes")
        
        data = parse_xsd(xsd_file)
        all_data[xsd_file.name] = data
        
        # Estadísticas
        print(f"   Tipos simples: {len(data['simple_types'])}")
        print(f"   Tipos complejos: {len(data['complex_types'])}")
        print(f"   Elementos: {len(data['elements'])}")
        
        # Tipos simples interesantes
        enums = [st for st in data['simple_types'] if st['enumerations']]
        if enums:
            print(f"   Enumeraciones:")
            for st in enums[:5]:
                print(f"     • {st['name']}: {st['base']} ({len(st['enumerations'])} valores)")
                for e in st['enumerations'][:3]:
                    print(f"       - {e}")
                if len(st['enumerations']) > 3:
                    print(f"       ... y {len(st['enumerations'])-3} más")
        
        # Tipos complejos importantes
        complex_names = [ct['name'] for ct in data['complex_types']]
        interesting = set(complex_names) & TIPO_INTERES
        if interesting:
            print(f"   Tipos complejos de interés:")
            for name in sorted(interesting):
                print(f"     ✓ {name}")
    
    return all_data


def generate_type_catalog(all_data):
    """Generar catálogo de tipos y sus restricciones."""
    print(f"\n{'='*70}")
    print(f"CATÁLOGO DE TIPOS DE DATOS DGII")
    print(f"{'='*70}\n")
    
    # Recopilar todos los tipos simples
    all_simple_types = {}
    for name, data in all_data.items():
        for st in data['simple_types']:
            all_simple_types[st['name']] = st
    
    # Tipos de enumeración (más importantes)
    enum_types = {name: st for name, st in all_simple_types.items() if st['enumerations']}
    
    print("📋 TIPOS DE ENUMERACIÓN (tipos de comprobante, impuestos, etc.):\n")
    
    for name, st in sorted(enum_types.items()):
        if len(st['enumerations']) <= 50:  # Solo mostrar enumeraciones razonables
            print(f"\n🔹 {name}")
            print(f"   Base: {st['base']}")
            print(f"   Valores ({len(st['enumerations'])}):")
            for e in st['enumerations']:
                print(f"     • {e}")
    
    # Patrones de validación
    print(f"\n{'='*70}")
    print(f"PATRONES DE VALIDACIÓN (regex)\n")
    print(f"{'='*70}\n")
    
    pattern_types = {name: st for name, st in all_simple_types.items() if st['patterns']}
    for name, st in sorted(pattern_types.items()):
        print(f"🔹 {name}")
        print(f"   Base: {st['base']}")
        for p in st['patterns']:
            print(f"   Pattern: {p}")
        print()


def main():
    if len(sys.argv) > 1:
        if sys.argv[1] == '--stats':
            analyze_all()
        elif sys.argv[1] == '--output' and len(sys.argv) > 2:
            # JSON output (no implementado en esta versión)
            print("Salida JSON no implementada en esta versión")
            sys.exit(1)
        else:
            print(f"Uso: {sys.argv[0]} [--stats|--output json]")
            sys.exit(1)
    else:
        all_data = analyze_all()
        generate_type_catalog(all_data)
        
        print(f"\n{'='*70}")
        print(f"✅ Análisis completado")
        print(f"{'='*70}\n")


if __name__ == '__main__':
    main()
