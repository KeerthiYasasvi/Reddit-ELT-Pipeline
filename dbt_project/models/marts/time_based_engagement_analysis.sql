{{ config(materialized='table') }}

with engagement_by_hour as (

  select
    t.hour_of_day,
    avg(f.engagement)         as avg_engagement,
    sum(f.engagement)         as total_engagement,
    count(*)                  as post_count
  from {{ ref('fact_posts') }} f
  join {{ ref('dim_time') }} t
    on f.time_id = t.time_id
  group by t.hour_of_day

)

select
  *
from engagement_by_hour
order by hour_of_day

/* To add day‑of‑week analysis, copy this model, group by t.day_of_week (add column to dim_time), and order by a numeric weekday column 
you add in dim_time */