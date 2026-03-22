<template>
  <div class="chart-container">
    <div
      v-if="title"
      class="chart-header"
    >
      <h4 class="chart-title">
        {{ title }}
      </h4>
      <span
        v-if="subtitle"
        class="chart-subtitle"
      >{{ subtitle }}</span>
    </div>
    
    <div class="chart-body">
      <!-- Horizontal Bar Chart -->
      <div
        v-if="type === 'bar'"
        class="bar-chart"
      >
        <div 
          v-for="(item, index) in data" 
          :key="index"
          class="bar-item"
        >
          <div class="bar-label">
            <span class="bar-label-text">{{ item.label }}</span>
            <span class="bar-value">{{ formatValue(item.value) }}</span>
          </div>
          <div class="bar-track">
            <div 
              class="bar-fill"
              :class="item.color || 'primary'"
              :style="{ width: getPercentage(item.value) + '%' }"
            >
              <span
                v-if="getPercentage(item.value) > 15"
                class="bar-fill-text"
              >
                {{ getPercentage(item.value) }}%
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- Donut Chart -->
      <div
        v-else-if="type === 'donut'"
        class="donut-chart-wrapper"
      >
        <svg
          class="donut-svg"
          viewBox="0 0 100 100"
        >
          <circle
            cx="50"
            cy="50"
            r="40"
            fill="none"
            stroke="var(--color-border)"
            stroke-width="12"
          />
          <circle
            v-for="(segment, index) in donutSegments"
            :key="index"
            cx="50"
            cy="50"
            r="40"
            fill="none"
            :stroke="segment.color"
            stroke-width="12"
            :stroke-dasharray="segment.dashArray"
            :stroke-dashoffset="segment.dashOffset"
            class="donut-segment"
          />
        </svg>
        <div class="donut-center">
          <span class="donut-total">{{ total }}</span>
          <span class="donut-label">{{ donutLabel }}</span>
        </div>
      </div>

      <!-- Line Chart -->
      <div
        v-else-if="type === 'line'"
        class="line-chart"
      >
        <svg
          class="line-svg"
          :viewBox="`0 0 ${width} ${height}`"
          preserveAspectRatio="none"
        >
          <defs>
            <linearGradient
              :id="`gradient-${uid}`"
              x1="0"
              x2="0"
              y1="0"
              y2="1"
            >
              <stop
                offset="0%"
                :stop-color="lineColor"
                stop-opacity="0.3"
              />
              <stop
                offset="100%"
                :stop-color="lineColor"
                stop-opacity="0"
              />
            </linearGradient>
          </defs>
          
          <!-- Grid lines -->
          <line 
            v-for="i in 4" 
            :key="'grid-' + i"
            :x1="0"
            :x2="width"
            :y1="(height / 4) * i"
            :y2="(height / 4) * i"
            class="grid-line"
          />
          
          <!-- Area fill -->
          <path
            :d="areaPath"
            :fill="`url(#gradient-${uid})`"
            class="area-fill"
          />
          
          <!-- Line -->
          <path
            :d="linePath"
            fill="none"
            :stroke="lineColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
            class="line-path"
          />
          
          <!-- Data points -->
          <circle
            v-for="(point, index) in points"
            :key="index"
            :cx="point.x"
            :cy="point.y"
            r="4"
            :fill="lineColor"
            class="data-point"
          />
        </svg>
        
        <div class="line-labels">
          <span
            v-for="(item, index) in data"
            :key="index"
            class="line-label"
          >
            {{ item.label }}
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  type: {
    type: String,
    default: 'bar',
    validator: (v) => ['bar', 'donut', 'line'].includes(v)
  },
  title: {
    type: String,
    default: ''
  },
  subtitle: {
    type: String,
    default: ''
  },
  data: {
    type: Array,
    required: true
  },
  maxValue: {
    type: Number,
    default: null
  },
  lineColor: {
    type: String,
    default: '#3b82f6'
  },
  width: {
    type: Number,
    default: 300
  },
  height: {
    type: Number,
    default: 120
  }
})

const uid = Math.random().toString(36).substr(2, 9)

const total = computed(() => {
  return props.data.reduce((sum, item) => sum + (item.value || 0), 0)
})

const max = computed(() => {
  if (props.maxValue) return props.maxValue
  return Math.max(...props.data.map(item => item.value || 0))
})

const getPercentage = (value) => {
  if (!value || max.value === 0) return 0
  return Math.round((value / max.value) * 100)
}

const formatValue = (value) => {
  if (value >= 1000) {
    return (value / 1000).toFixed(1) + 'k'
  }
  return value
}

// Donut chart calculations
const donutSegments = computed(() => {
  const circumference = 2 * Math.PI * 40
  let offset = 0
  
  return props.data.map(item => {
    const percentage = total.value > 0 ? item.value / total.value : 0
    const dashLength = circumference * percentage
    const segment = {
      color: item.color || '#3b82f6',
      dashArray: `${dashLength} ${circumference - dashLength}`,
      dashOffset: -offset
    }
    offset += dashLength
    return segment
  })
})

const donutLabel = computed(() => {
  return props.title || 'Total'
})

// Line chart calculations
const points = computed(() => {
  const xStep = props.width / (props.data.length - 1 || 1)
  const yScale = props.height / (max.value || 1)
  
  return props.data.map((item, index) => ({
    x: index * xStep,
    y: props.height - (item.value || 0) * yScale
  }))
})

const linePath = computed(() => {
  if (points.value.length === 0) return ''
  return points.value.map((p, i) => 
    i === 0 ? `M ${p.x} ${p.y}` : `L ${p.x} ${p.y}`
  ).join(' ')
})

const areaPath = computed(() => {
  if (points.value.length === 0) return ''
  const line = linePath.value
  const lastX = points.value[points.value.length - 1]?.x || 0
  const firstX = points.value[0]?.x || 0
  return `${line} L ${lastX} ${props.height} L ${firstX} ${props.height} Z`
})
</script>

<style scoped>
.chart-container {
  background: var(--color-surface);
  border-radius: var(--radius-lg);
  padding: 1.25rem;
  border: 1px solid var(--color-border);
}

.chart-header {
  margin-bottom: 1rem;
}

.chart-title {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--color-text-primary);
  margin: 0;
}

.chart-subtitle {
  font-size: 0.75rem;
  color: var(--color-text-muted);
  margin-top: 0.25rem;
  display: block;
}

/* Bar Chart */
.bar-chart {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.bar-item {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.bar-label {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.bar-label-text {
  font-size: 0.8rem;
  color: var(--color-text-secondary);
}

.bar-value {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--color-text-primary);
}

.bar-track {
  height: 8px;
  background: var(--color-surface-hover);
  border-radius: 4px;
  overflow: hidden;
}

.bar-fill {
  height: 100%;
  border-radius: 4px;
  transition: width 0.6s cubic-bezier(0.4, 0, 0.2, 1);
  display: flex;
  align-items: center;
  justify-content: flex-end;
  padding-right: 0.5rem;
  min-width: 0;
}

.bar-fill.primary { background: linear-gradient(90deg, #2563eb, #4f46e5); }
.bar-fill.success { background: linear-gradient(90deg, #16a34a, #22c55e); }
.bar-fill.warning { background: linear-gradient(90deg, #d97706, #f59e0b); }
.bar-fill.danger { background: linear-gradient(90deg, #dc2626, #ef4444); }
.bar-fill.info { background: linear-gradient(90deg, #0891b2, #06b6d4); }
.bar-fill.purple { background: linear-gradient(90deg, #7c3aed, #a855f7); }

.bar-fill-text {
  font-size: 0.65rem;
  font-weight: 700;
  color: white;
}

/* Donut Chart */
.donut-chart-wrapper {
  position: relative;
  width: 160px;
  height: 160px;
  margin: 0 auto;
}

.donut-svg {
  transform: rotate(-90deg);
}

.donut-segment {
  transition: stroke-dasharray 0.6s ease;
}

.donut-center {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  text-align: center;
}

.donut-total {
  display: block;
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--color-text-primary);
  line-height: 1;
}

.donut-label {
  display: block;
  font-size: 0.7rem;
  color: var(--color-text-muted);
  margin-top: 0.25rem;
}

/* Line Chart */
.line-chart {
  width: 100%;
}

.line-svg {
  width: 100%;
  height: 120px;
}

.grid-line {
  stroke: var(--color-border);
  stroke-width: 1;
  stroke-dasharray: 4 2;
}

.line-path {
  filter: drop-shadow(0 2px 4px rgba(37, 99, 235, 0.2));
}

.data-point {
  transition: r 0.2s ease;
}

.data-point:hover {
  r: 6;
}

.line-labels {
  display: flex;
  justify-content: space-between;
  margin-top: 0.5rem;
  padding: 0 0.25rem;
}

.line-label {
  font-size: 0.65rem;
  color: var(--color-text-muted);
}
</style>
