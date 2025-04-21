{{ config(materialized='table') }}

with overall_avg as (
  select avg(engagement) as avg_engagement
  from {{ ref('fact_posts') }}
),

candidates as (
  select *
  from {{ ref('posts_summary') }}
  where engagement > (select avg_engagement from overall_avg)
)

select * from candidates
order by engagement desc