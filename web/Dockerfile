# Build: docker build -f web/Dockerfile web   (VITE_* are baked in at build time)
FROM node:22-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
ARG VITE_OIDC_AUTHORITY
ARG VITE_OIDC_CLIENT_ID=tyhr-web
ENV VITE_OIDC_AUTHORITY=$VITE_OIDC_AUTHORITY VITE_OIDC_CLIENT_ID=$VITE_OIDC_CLIENT_ID
RUN npm run build

# Static files only; Caddy (deploy/caddy) serves them and routes /api to the API.
FROM caddy:2-alpine
COPY --from=build /src/dist /srv
COPY Caddyfile.static /etc/caddy/Caddyfile
