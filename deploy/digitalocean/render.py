#!/usr/bin/env python3
"""Render the private compatibility slice; kubectl owns application resources.
Secrets are installed separately from protected files. No cloud calls here.
"""
import argparse
import ipaddress
import json
import pathlib
import re


def render(inventory, image, egress_rules, allowed_hosts, proxy_cidr):
    campaign = inventory['campaign']
    if not re.fullmatch(r'locintel-compat-[a-z0-9-]{1,24}', campaign):
        raise ValueError('invalid campaign name')
    if not re.fullmatch(r'[^\s]+@sha256:[0-9a-f]{64}', image):
        raise ValueError('application image must be pinned by digest')
    hosts = allowed_hosts.split(';')
    if not hosts or any(not re.fullmatch(r'(?:\*\.)?[a-zA-Z0-9.-]+', host) for host in hosts):
        raise ValueError('allowed hosts must be explicit DNS names or suffix wildcards')
    if ipaddress.ip_network(proxy_cidr).prefixlen == 0:
        raise ValueError('trusted proxy CIDR cannot trust every address')
    ipaddress.ip_address(inventory['nfs_host'])
    if not inventory['nfs_path'].startswith('/'):
        raise ValueError('NFS export path must be absolute')
    # Native egress rules are supplied from the actual campaign's approved endpoints.
    # Refuse wildcard destinations/ports instead of silently allowing public egress.
    if not isinstance(egress_rules, list) or not egress_rules:
        raise ValueError('provide explicit database/provider egress rules')
    for rule in egress_rules:
        if set(rule) != {'to', 'ports'} or not rule['to'] or not rule['ports']:
            raise ValueError('egress rules require explicit destinations and ports')
        for peer in rule['to']:
            if set(peer) != {'ipBlock'} or set(peer['ipBlock']) != {'cidr'}:
                raise ValueError('campaign egress destinations must be explicit CIDRs')
            network = ipaddress.ip_network(peer['ipBlock']['cidr'])
            if network.prefixlen == 0 or network.is_multicast or network.is_unspecified:
                raise ValueError('unrestricted egress destination refused')
        for port in rule['ports']:
            if set(port) != {'protocol', 'port'} or port['protocol'] not in ['TCP', 'UDP'] or type(port['port']) is not int or not 1 <= port['port'] <= 65535:
                raise ValueError('egress requires explicit TCP/UDP ports')
    resources = [{'apiVersion': 'v1', 'kind': 'Namespace', 'metadata': {'name': campaign, 'labels': {
        # Inline NFS is not permitted by Restricted; baseline still forbids privileged/host access.
        'pod-security.kubernetes.io/enforce': 'baseline',
        'pod-security.kubernetes.io/enforce-version': 'v1.36',
    }}}]

    def resource(kind, name, spec=None, **extra):
        obj = {'apiVersion': 'apps/v1' if kind == 'Deployment' else 'batch/v1' if kind == 'Job' else 'networking.k8s.io/v1' if kind == 'NetworkPolicy' else 'v1',
               'kind': kind, 'metadata': {'name': name, 'namespace': campaign}, **extra}
        if spec is not None:
            obj['spec'] = spec
        resources.append(obj)

    resource('NetworkPolicy', 'default-deny', {'podSelector': {}, 'policyTypes': ['Ingress', 'Egress']})
    roles = {'matchExpressions': [{'key': 'app', 'operator': 'In', 'values': ['api', 'worker', 'migrate']}]}
    resource('NetworkPolicy', 'application-egress', {'podSelector': roles, 'policyTypes': ['Egress'],
        'egress': [
            {'to': [{'namespaceSelector': {'matchLabels': {'kubernetes.io/metadata.name': 'kube-system'}},
                     'podSelector': {'matchLabels': {'k8s-app': 'kube-dns'}}}],
             'ports': [{'protocol': protocol, 'port': 53} for protocol in ['UDP', 'TCP']]},
            {'to': [{'podSelector': {'matchLabels': {'app': 'scanner'}}}],
             'ports': [{'protocol': 'TCP', 'port': 3310}]},
            *egress_rules]})
    resource('NetworkPolicy', 'gateway-to-api', {'podSelector': {'matchLabels': {'app': 'api'}},
        'policyTypes': ['Ingress'], 'ingress': [{'from': [{'podSelector': {'matchLabels': {'app': 'gateway'}}}],
                                               'ports': [{'protocol': 'TCP', 'port': 8080}]}]})
    resource('NetworkPolicy', 'scanner-ingress', {'podSelector': {'matchLabels': {'app': 'scanner'}},
        'policyTypes': ['Ingress'], 'ingress': [{'from': [{'podSelector': roles}],
                                               'ports': [{'protocol': 'TCP', 'port': 3310}]}]})

    def pod(role):
        return {'automountServiceAccountToken': False,
                'imagePullSecrets': [{'name': 'registry'}],
                'securityContext': {'runAsNonRoot': True, 'runAsUser': 1654, 'runAsGroup': 1654, 'fsGroup': 1654, 'seccompProfile': {'type': 'RuntimeDefault'}},
                'containers': [{'name': 'locintel', 'image': image,
                    'securityContext': {'allowPrivilegeEscalation': False, 'readOnlyRootFilesystem': True, 'capabilities': {'drop': ['ALL']}},
                    'envFrom': [{'secretRef': {'name': 'providers'}},
                                {'secretRef': {'name': 'migration-database' if role == 'migrate' else 'app-database'}}],
                    'env': [{'name': k, 'value': v} for k, v in {
                        'ROLE': role, 'ASPNETCORE_ENVIRONMENT': 'Production',
                        'ASPNETCORE_URLS': 'http://+:8080',
                        'AllowedHosts': allowed_hosts + ';health.locintel.internal',
                        'Proxy__TrustForwardedHeaders': 'true', 'Proxy__KnownNetworks__0': proxy_cidr,
                        'DataProtection__KeyPath': '/keys',
                        'DataProtection__CertificatePath': '/certificate/keyring.pfx',
                        'Gateway__Required': 'true', 'Traffic__MaxConcurrentRequests': '32',
                    }.items()],
                    'ports': [{'containerPort': 8080}],
                    'resources': {'requests': {'cpu': '500m', 'memory': '512Mi'},
                                  'limits': {'cpu': '1', 'memory': '2Gi'}},
                    'volumeMounts': [{'name': 'keys', 'mountPath': '/keys'},
                                     {'name': 'temporary', 'mountPath': '/tmp'},
                                     {'name': 'certificate', 'mountPath': '/certificate', 'readOnly': True},
                                     {'name': 'database-ca', 'mountPath': '/database-ca', 'readOnly': True}]}],
                'volumes': [{'name': 'temporary', 'emptyDir': {'sizeLimit': '256Mi'}},
                            {'name': 'keys', 'nfs': {'server': inventory['nfs_host'], 'path': inventory['nfs_path']}},
                            {'name': 'certificate', 'secret': {'secretName': 'keyring-certificate', 'defaultMode': 0o440}},
                            {'name': 'database-ca', 'secret': {'secretName': 'database-ca'}}]}

    migrate = pod('migrate')
    migrate['restartPolicy'] = 'Never'
    resource('Job', 'migrate', {'backoffLimit': 0, 'activeDeadlineSeconds': 600,
                              'template': {'metadata': {'labels': {'app': 'migrate'}}, 'spec': migrate}})
    for role, replicas in [('api', 2), ('worker', 1)]:
        spec = pod(role)
        spec['terminationGracePeriodSeconds'] = 120
        spec['containers'][0].update({
            'startupProbe': {'httpGet': {'path': '/livez', 'port': 8080, 'httpHeaders': [{'name': 'Host', 'value': 'health.locintel.internal'}]}, 'periodSeconds': 2, 'failureThreshold': 90},
            'readinessProbe': {'httpGet': {'path': '/healthz', 'port': 8080, 'httpHeaders': [{'name': 'Host', 'value': 'health.locintel.internal'}]}, 'periodSeconds': 5},
            'livenessProbe': {'httpGet': {'path': '/livez', 'port': 8080, 'httpHeaders': [{'name': 'Host', 'value': 'health.locintel.internal'}]}, 'periodSeconds': 10}})
        if role == 'api':
            spec['affinity'] = {'podAntiAffinity': {'requiredDuringSchedulingIgnoredDuringExecution': [
                {'labelSelector': {'matchLabels': {'app': 'api'}}, 'topologyKey': 'kubernetes.io/hostname'}]}}
        resource('Deployment', role, {'replicas': replicas, 'selector': {'matchLabels': {'app': role}},
            # Two fixed nodes cannot schedule a third anti-affine API. This private
            # bootstrap uses Recreate; rolling-update qualification is a later slice.
            'strategy': {'type': 'Recreate'},
            'template': {'metadata': {'labels': {'app': role}}, 'spec': spec}})
    resource('Service', 'api', {'clusterIP': 'None', 'selector': {'app': 'api'},
                              'ports': [{'port': 8080, 'targetPort': 8080}]})
    return {'apiVersion': 'v1', 'kind': 'List', 'items': resources}


def check():
    sample = {'campaign': 'locintel-compat-check', 'nfs_host': '10.0.0.2', 'nfs_path': '/keys'}
    image = 'example.test/locintel@sha256:' + 'a' * 64
    egress = [{'to': [{'ipBlock': {'cidr': '10.0.0.3/32'}}], 'ports': [{'protocol': 'TCP', 'port': 25060}]}]
    items = render(sample, image, egress, 'console.example.test;*.example.test', '10.0.1.0/24')['items']
    job = next(x for x in items if x['kind'] == 'Job')
    api = next(x for x in items if x['kind'] == 'Deployment' and x['metadata']['name'] == 'api')
    assert 'migration-database' in json.dumps(job)
    assert 'migration-database' not in json.dumps(api)
    assert api['spec']['replicas'] == 2 and 'requiredDuringSchedulingIgnoredDuringExecution' in json.dumps(api)
    assert all(x['spec'].get('type') != 'LoadBalancer' for x in items if x['kind'] == 'Service')
    for invalid in ['example.test/locintel:latest', 'example.test/locintel@sha256:bad']:
        try:
            render(sample, invalid, egress, 'console.example.test', '10.0.1.0/24')
        except ValueError:
            pass
        else:
            raise AssertionError('unpinned image accepted')
    policies = {x['metadata']['name']: x['spec'] for x in items if x['kind'] == 'NetworkPolicy'}
    assert policies['default-deny'] == {'podSelector': {}, 'policyTypes': ['Ingress', 'Egress']}
    assert policies['application-egress']['egress'][-1] == egress[0]
    assert policies['gateway-to-api']['ingress'][0]['from'] == [{'podSelector': {'matchLabels': {'app': 'gateway'}}}]
    for workload in [x for x in items if x['kind'] in ['Job', 'Deployment']]:
        pod_spec = workload['spec']['template']['spec']
        assert pod_spec['securityContext']['seccompProfile']['type'] == 'RuntimeDefault'
        assert pod_spec['containers'][0]['securityContext']['readOnlyRootFilesystem']
        assert not pod_spec['automountServiceAccountToken']
    for invalid in [[], [{}], [{'to': [{'ipBlock': {'cidr': '0.0.0.0/0'}}], 'ports': [{'protocol': 'TCP', 'port': 443}]}],
                    [{'to': [{'ipBlock': {'cidr': '10.0.0.3/32'}}], 'ports': []}]]:
        try:
            render(sample, image, invalid, 'console.example.test', '10.0.1.0/24')
        except ValueError:
            pass
        else:
            raise AssertionError('unrestricted/missing egress accepted')
    for hosts, proxy in [('*', '10.0.1.0/24'), ('https://example.test', '10.0.1.0/24'), ('example.test', '0.0.0.0/0')]:
        try:
            render(sample, image, egress, hosts, proxy)
        except ValueError:
            pass
        else:
            raise AssertionError('unbounded host/proxy trust accepted')
    print('private topology, least privilege, network isolation and image checks passed')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--inventory', type=pathlib.Path)
    parser.add_argument('--image')
    parser.add_argument('--allowed-hosts', help='semicolon-separated application DNS hosts')
    parser.add_argument('--proxy-cidr', help='approved network of the immediate gateway peers')
    parser.add_argument('--egress-rules', type=pathlib.Path, help='JSON array of narrow native NetworkPolicy egress rules')
    parser.add_argument('--phase', choices=['migrate', 'workloads'], required=False)
    args = parser.parse_args()
    if args.check:
        check()
    else:
        if not all([args.inventory, args.image, args.egress_rules, args.allowed_hosts, args.proxy_cidr]):
            parser.error('--inventory, --image, --egress-rules, --allowed-hosts and --proxy-cidr are required')
        if not args.phase:
            parser.error('--phase migrate or workloads is required; migration must complete first')
        result = render(json.loads(args.inventory.read_text()), args.image, json.loads(args.egress_rules.read_text()), args.allowed_hosts, args.proxy_cidr)
        result['items'] = [x for x in result['items'] if x['kind'] in ['Namespace', 'NetworkPolicy'] or (x['kind'] == 'Job') == (args.phase == 'migrate')]
        print(json.dumps(result, indent=2))
