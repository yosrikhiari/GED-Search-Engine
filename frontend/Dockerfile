FROM node:20-alpine AS build
WORKDIR /app

ENV NODE_OPTIONS=--max-old-space-size=2048

ARG VITE_API_BASE_URL=""
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

COPY package*.json ./

# Install ALL deps including devDependencies (tailwind, postcss, autoprefixer)
RUN npm install --legacy-peer-deps --no-audit --no-fund

COPY . .

# Verify Tailwind can see your source files before building
RUN echo "Checking Tailwind content files:" && \
    find src -name "*.vue" | head -5 && \
    cat Tailwind.config.js

RUN npm run build

# Verify CSS was generated
RUN echo "Built assets:" && ls -lh dist/assets/

FROM nginx:alpine AS production
WORKDIR /usr/share/nginx/html

RUN rm -rf ./*

COPY --from=build /app/dist .
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD wget --quiet --tries=1 --spider http://localhost/ || exit 1

CMD ["nginx", "-g", "daemon off;"]