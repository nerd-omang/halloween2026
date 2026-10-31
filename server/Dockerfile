FROM node:22-alpine
ENV NODE_ENV=production
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY src ./src
USER node
EXPOSE 8080
# A small heap is plenty for a counter service and keeps the 256 MB VM comfortable.
CMD ["node", "--max-old-space-size=96", "src/server.js"]
