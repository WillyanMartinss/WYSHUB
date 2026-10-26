#!/usr/bin/env python3
"""
Wrapper simples para `signtool.exe sign` com verificação e entrada segura de senha.

Uso:
  python signtool.py --cert Cert.pfx --file App.exe --password S3nh4
  python signtool.py -f Cert.pfx -t http://timestamp.digicert.com App.exe

O script verifica se `signtool.exe` está no PATH, solicita senha se não for passada,
e executa a assinatura usando SHA256 com timestamp.
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import os
from getpass import getpass


def find_signtool() -> str | None:
    """Retorna o caminho para signtool.exe ou None se não encontrado."""
    return shutil.which('signtool.exe')


def build_command(signtool_path: str, cert_path: str, cert_password: str, target_file: str, timestamp_url: str) -> list[str]:
    cmd = [
        signtool_path,
        'sign',
        '/fd', 'SHA256',
        '/tr', timestamp_url,
        '/td', 'SHA256',
        '/f', cert_path,
        '/p', cert_password,
        target_file,
    ]
    return cmd


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description='Wrapper para signtool.exe (sign)')
    parser.add_argument('-f', '--cert', required=True, help='Caminho para o arquivo .pfx')
    parser.add_argument('-p', '--password', help='Senha do certificado (se omitido, será solicitado)')
    parser.add_argument('-t', '--timestamp', default='http://timestamp.digicert.com', help='URL de timestamp (padrão: %(default)s)')
    parser.add_argument('target', help='Arquivo a assinar (ex: App.exe)')

    args = parser.parse_args(argv)

    cert_path = os.path.expanduser(args.cert)
    if not os.path.isfile(cert_path):
        print(f'Certificado não encontrado: {cert_path}', file=sys.stderr)
        return 2

    target = os.path.expanduser(args.target)
    if not os.path.isfile(target):
        print(f'Alvo não encontrado: {target}', file=sys.stderr)
        return 3

    password = args.password
    if not password:
        try:
            password = getpass('Senha do certificado: ')
        except (KeyboardInterrupt, EOFError):
            print('\nEntrada cancelada.', file=sys.stderr)
            return 4

    signtool = find_signtool()
    if not signtool:
        print('signtool.exe não encontrado no PATH. Instale o Windows SDK ou adicione signtool.exe ao PATH.', file=sys.stderr)
        return 5

    cmd = build_command(signtool, cert_path, password, target, args.timestamp)

    # Mostramos comando sem a senha para diagnóstico
    safe_cmd = [c for c in cmd]
    try:
        p_index = safe_cmd.index('/p')
        safe_cmd[p_index+1] = '*****'
    except Exception:
        pass

    print('Executando: ' + ' '.join(safe_cmd))

    try:
        proc = subprocess.run(cmd, check=False, capture_output=True, text=True)
    except Exception as ex:
        print(f'Falha ao executar signtool: {ex}', file=sys.stderr)
        return 6

    sys.stdout.write(proc.stdout)
    sys.stderr.write(proc.stderr)

    if proc.returncode != 0:
        print(f'signtool retornou código {proc.returncode}', file=sys.stderr)
    else:
        print('Assinatura concluída com sucesso.')

    return proc.returncode


if __name__ == '__main__':
    raise SystemExit(main())
