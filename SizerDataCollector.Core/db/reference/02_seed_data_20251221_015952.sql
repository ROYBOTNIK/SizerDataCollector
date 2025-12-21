--
-- PostgreSQL database dump
--

-- Dumped from database version 17.4
-- Dumped by pg_dump version 17.4

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Data for Name: band_definitions; Type: TABLE DATA; Schema: oee; Owner: -
--



--
-- Data for Name: band_statistics; Type: TABLE DATA; Schema: oee; Owner: -
--



--
-- Data for Name: grade_lane_anomalies; Type: TABLE DATA; Schema: oee; Owner: -
--



--
-- Data for Name: machine_thresholds; Type: TABLE DATA; Schema: oee; Owner: -
--

INSERT INTO oee.machine_thresholds (serial_no, min_rpm, min_total_fpm, updated_at) VALUES ('140578', 1500, 1000, '2025-05-30 05:44:53.659635+12');


--
-- Data for Name: shift_calendar; Type: TABLE DATA; Schema: oee; Owner: -
--



--
-- Data for Name: machine_settings; Type: TABLE DATA; Schema: public; Owner: -
--

INSERT INTO public.machine_settings (id, target_machine_speed, lane_count, target_percentage, recycle_outlet) VALUES (2, 2050, 32, 85, 0);
INSERT INTO public.machine_settings (id, target_machine_speed, lane_count, target_percentage, recycle_outlet) VALUES (1, 2050, 32, 85, 0);


--
-- Name: machine_settings_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.machine_settings_id_seq', 2, true);


--
-- PostgreSQL database dump complete
--

