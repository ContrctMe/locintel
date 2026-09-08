import { request as httpRequest } from 'node:http';
import { request as httpsRequest } from 'node:https';
import { Readable } from 'node:stream';

/** Server-only, bodyless upstream requests. Node fetch discards Host overrides. */
export function fetchWithHost(url: string, init: {
  method?: 'GET' | 'POST';
  headers: Record<string, string>;
  signal: AbortSignal;
  redirect?: 'manual';
}): Promise<Response> {
  const target = new URL(url);
  if (!['http:', 'https:'].includes(target.protocol)) throw new TypeError('Unsupported upstream protocol');
  return new Promise((resolve, reject) => {
    const request = (target.protocol === 'https:' ? httpsRequest : httpRequest)(target, {
      method: init.method ?? 'GET', headers: { ...init.headers, 'Accept-Encoding': 'identity' }, signal: init.signal,
    }, incoming => {
      try {
        const headers = new Headers();
        for (let i = 0; i < incoming.rawHeaders.length; i += 2)
          headers.append(incoming.rawHeaders[i]!, incoming.rawHeaders[i + 1]!);
        const status = incoming.statusCode!;
        const empty = [204, 205, 304].includes(status);
        if (empty) incoming.resume();
        let bytes = 0;
        const body = empty ? null : (Readable.toWeb(incoming, {
          strategy: { highWaterMark: incoming.readableHighWaterMark, size: (chunk: Uint8Array) => chunk.byteLength },
        }) as ReadableStream<Uint8Array>).pipeThrough(new TransformStream<Uint8Array, Uint8Array>({
          transform(chunk, controller) {
            bytes += chunk.byteLength;
            if (bytes > 4 * 1024 * 1024) {
              incoming.destroy();
              controller.error(new Error('Upstream response exceeds 4 MiB'));
            } else controller.enqueue(chunk);
          },
        }));
        resolve(new Response(body, { status, headers }));
      } catch (error) {
        incoming.destroy();
        reject(error);
      }
    });
    request.on('error', reject);
    request.end();
  });
}
