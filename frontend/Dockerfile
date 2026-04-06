FROM node:20-alpine AS build
WORKDIR /app

ENV NODE_OPTIONS=--max-old-space-size=2048

ARG VITE_API_BASE_URL=""
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

COPY package*.json ./
RUN npm install --legacy-peer-deps --no-audit --no-fund

COPY . .

# If dist already exists locally, skip rebuild
RUN if [ -d dist ] && [ -f dist/index.html ]; then \
      echo "Using existing dist folder"; \
    else \
      echo "Building frontend..." && npm run build; \
    fi

FROM nginx:alpine AS production
WORKDIR /usr/share/nginx/html

RUN rm -rf ./*

COPY --from=build /app/dist .
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD wget --quiet --tries=1 --spider http://localhost/ || exit 1

CMD ["nginx", "-g", "daemon off;"]