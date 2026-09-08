FROM node:22-alpine AS build
WORKDIR /app
RUN corepack enable && corepack prepare pnpm@9.15.9 --activate
COPY . .
RUN pnpm install --frozen-lockfile \
    && pnpm --filter @locintel/console build \
    && pnpm --filter @locintel/public build \
    && pnpm --filter @locintel/public deploy --prod /runtime

FROM node:22-alpine AS public
WORKDIR /app
COPY --from=build --chown=node:node /runtime ./
COPY --from=build --chown=node:node /app/apps/public/dist ./dist
ENV NODE_ENV=production HOST=0.0.0.0 PORT=3000
USER node
EXPOSE 3000
CMD ["node", "node_modules/srvx/bin/srvx.mjs", "serve", "--prod", "--entry", "dist/server/server.js", "--static", "../client"]

FROM caddy:2-alpine AS console
RUN setcap -r /usr/bin/caddy
COPY --from=build /app/apps/console/dist /srv
COPY Caddyfile /etc/caddy/Caddyfile
ENV XDG_CONFIG_HOME=/tmp
USER 1000:1000
EXPOSE 8080
