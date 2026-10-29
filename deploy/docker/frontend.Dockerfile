FROM node:22-alpine AS build
WORKDIR /app
COPY src/frontend/package*.json ./
RUN npm install
COPY src/frontend/ ./
ARG VITE_API_BASE_URL=
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}
# Empty means same-origin: Nginx routes /api/orders to the order service and everything else under /api to the order builder's API.
ARG VITE_ORDER_SERVICE_URL=
ENV VITE_ORDER_SERVICE_URL=${VITE_ORDER_SERVICE_URL}
RUN npm run build

FROM nginx:1.28-alpine AS final
COPY deploy/nginx/frontend.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=15s --timeout=3s --retries=5 CMD wget -q -O /dev/null http://127.0.0.1/healthz || exit 1
